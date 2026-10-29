using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Bybit;
using ProfitGraph.Venues.Bybit.MarketData;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     The Bybit Spot order gateway against the stateful mock exchange (ADR 0017, Profit Fox Designer ADR 0055). Keys are
///     invented and nothing leaves the process.
/// </summary>
public sealed class BybitOrderGatewayTests
{
    private BybitGatewayHarness _harness = null!;
    private int _placeStarts;

    [SetUp]
    public void Create()
    {
        _harness = new BybitGatewayHarness();
        _placeStarts = 0;
    }

    [Test]
    public void ClientOrderIdIsLosslessAndExactly36Characters()
    {
        var bot = BotId.New();

        ClientOrderId id = BybitClientOrderIds.For(bot, engineOrderId: 1_234_567);

        id.Value.Length.ShouldBe(36);
        id.Value.ShouldStartWith(BybitClientOrderIds.PrefixOf(bot));
        BybitClientOrderIds.TryParse(id.Value, out BotId? parsed, out long order).ShouldBeTrue();
        parsed.ShouldBe(bot);
        order.ShouldBe(1_234_567);
    }

    [TestCase("pf-0123")]
    [TestCase("")]
    [TestCase("pgAAAAAAAAAAAAAAAAAAAAAA-AAAAAAAAAAA")]
    public void ForeignOrMalformedClientOrderIdIsNotOurs(string value)
    {
        BybitClientOrderIds.TryParse(value, out _, out _).ShouldBeFalse();
    }

    [Test]
    public async Task AcceptedSubmitIsSignedSizedInQuoteAndMarkedBeforeItIsSent()
    {
        ExternalOrderUpdate? rejection = await Submit(BybitGatewayHarness.MarketBuy(id: 1, quoteBudget: 12.3456789m));

        rejection.ShouldBeNull();
        _placeStarts.ShouldBe(1);
        JsonElement body = JsonDocument.Parse(_harness.Exchange.CreateBodies.Single()).RootElement;
        body.GetProperty("marketUnit").GetString().ShouldBe("quoteCoin");
        body.GetProperty("qty").GetString().ShouldBe("12.34");
        body.GetProperty("orderLinkId").GetString().ShouldBe(BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 1).Value);
    }

    [Test]
    public async Task LimitOrderRoundsQuantityAndPriceDown()
    {
        await Submit(BybitGatewayHarness.LimitSell(id: 2, quantity: 0.123456m, price: 101.239m));

        JsonElement body = JsonDocument.Parse(_harness.Exchange.CreateBodies.Single()).RootElement;
        body.GetProperty("qty").GetString().ShouldBe("0.1234");
        body.GetProperty("price").GetString().ShouldBe("101.23");
        body.GetProperty("side").GetString().ShouldBe("Sell");
    }

    [Test]
    public async Task OrderBelowTheMinimumIsRejectedWithoutBeingSent()
    {
        ExternalOrderUpdate? rejection = await Submit(BybitGatewayHarness.MarketBuy(id: 1, quoteBudget: 4m));

        rejection!.Reason.ShouldBe(RejectionReason.BelowMinNotional);
        _placeStarts.ShouldBe(0);
        _harness.Exchange.CreateBodies.ShouldBeEmpty();
    }

    [TestCase(arg1: 110007, RejectionReason.InsufficientQuote)]
    [TestCase(arg1: 110003, RejectionReason.InvalidPrice)]
    [TestCase(arg1: 110017, RejectionReason.ZeroQuantity)]
    public async Task ExplicitBybitRejectionRejects(int code, RejectionReason reason)
    {
        _harness.Exchange.CreateCode = code;

        ExternalOrderUpdate? rejection = await Submit(BybitGatewayHarness.MarketBuy(id: 1, quoteBudget: 10m));

        rejection!.Reason.ShouldBe(reason);
        rejection.Kind.ShouldBe(ExternalOrderUpdateKind.Rejected);
    }

    [Test]
    public async Task UnknownOutcomeIsNeverARejection()
    {
        _harness.Exchange.CreateUnavailable = true;

        await Should.ThrowAsync<BybitOrderUnresolvedException>(() => Submit(BybitGatewayHarness.MarketBuy(id: 1, quoteBudget: 10m)));

        _placeStarts.ShouldBe(1);
    }

    [Test]
    public async Task UnknownRetCodeIsNeverARejection()
    {
        _harness.Exchange.CreateCode = 10006;

        await Should.ThrowAsync<BybitOrderUnresolvedException>(() => Submit(BybitGatewayHarness.MarketBuy(id: 1, quoteBudget: 10m)));
    }

    [Test]
    public async Task RetryLooksTheOrderUpAndPublishesItsStateWithoutPlacing()
    {
        string clientOrderId = BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 3).Value;
        _harness.Exchange.AddOrder(clientOrderId, "Buy", "Filled", cumulativeBase: 0.1m, cumulativeQuote: 10m, new Dictionary<string, decimal> { ["BTC"] = 0.0001m });

        ExternalOrderUpdate? result = await Submit(BybitGatewayHarness.MarketBuy(id: 3, quoteBudget: 10m), mustLookUp: true);

        result.ShouldBeNull();
        _placeStarts.ShouldBe(0);
        _harness.Exchange.CreateBodies.ShouldBeEmpty();
        List<ExternalOrderUpdate> published = await _harness.Published(count: 2);
        published[0].ShouldBe(ExternalOrderUpdate.Accept(orderId: 3));
        published[1].ShouldBe(ExternalOrderUpdate.Final(orderId: 3, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
    }

    [Test]
    public async Task EmptyLookupAfterAPossibleSendIsNotProofOfAbsence()
    {
        await Should.ThrowAsync<BybitOrderUnresolvedException>(() => Submit(BybitGatewayHarness.MarketBuy(id: 4, quoteBudget: 10m), mustLookUp: true));

        _harness.Exchange.CreateBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task DuplicateResponseLooksTheOrderUp()
    {
        string clientOrderId = BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 5).Value;
        _harness.Exchange.AddOrder(clientOrderId, "Buy", "New", cumulativeBase: 0m, cumulativeQuote: 0m, []);

        ExternalOrderUpdate? result = await Submit(BybitGatewayHarness.MarketBuy(id: 5, quoteBudget: 10m));

        result.ShouldBeNull();
        (await _harness.Published(count: 1)).Single().ShouldBe(ExternalOrderUpdate.Accept(orderId: 5));
    }

    [Test]
    public async Task FeeInAnotherAssetCannotBeAccounted()
    {
        string clientOrderId = BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 6).Value;
        _harness.Exchange.AddOrder(clientOrderId, "Buy", "Filled", cumulativeBase: 0.1m, cumulativeQuote: 10m, new Dictionary<string, decimal> { ["MNT"] = 0.01m });

        await Should.ThrowAsync<BybitUnsupportedFeeAssetException>(() => Submit(BybitGatewayHarness.MarketBuy(id: 6, quoteBudget: 10m), mustLookUp: true));
    }

    [Test]
    public async Task CancelOfAnOpenOrderIsAccepted()
    {
        string clientOrderId = BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 7).Value;
        _harness.Exchange.AddOrder(clientOrderId, "Sell", "New", cumulativeBase: 0m, cumulativeQuote: 0m, []);

        await _harness.Gateway.Cancel(_harness.Bot.Id, new ClientOrderId(clientOrderId), CancellationToken.None);

        _harness.Exchange.Requests.ShouldContain((HttpMethod.Post, "/v5/order/cancel"));
    }

    [Test]
    public async Task RefusedCancelOfAFinalOrderPublishesItsState()
    {
        string clientOrderId = BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 8).Value;
        _harness.Exchange.AddOrder(clientOrderId, "Sell", "Filled", cumulativeBase: 0.1m, cumulativeQuote: 10m, new Dictionary<string, decimal> { ["USDT"] = 0.01m });
        _harness.Exchange.CancelCode = 110001;

        await _harness.Gateway.Cancel(_harness.Bot.Id, new ClientOrderId(clientOrderId), CancellationToken.None);

        (await _harness.Published(count: 2))[1].ShouldBe(ExternalOrderUpdate.Final(orderId: 8, grossBase: 0.1m, grossQuote: 10m, fee: 0.01m));
    }

    [Test]
    public async Task RefusedCancelOfAnActiveOrderStaysUnresolved()
    {
        string clientOrderId = BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 9).Value;
        _harness.Exchange.AddOrder(clientOrderId, "Sell", "New", cumulativeBase: 0m, cumulativeQuote: 0m, []);
        _harness.Exchange.CancelCode = 10006;

        await Should.ThrowAsync<BybitOrderUnresolvedException>(() => _harness.Gateway.Cancel(_harness.Bot.Id, new ClientOrderId(clientOrderId), CancellationToken.None));
    }

    [Test]
    public async Task FeeRatesAreReadWithTheCredential()
    {
        VenueFeeRatesInternal rates = await _harness.FeeRates.Read(
            new ExchangeCredentialSecretsInternal(Guid.NewGuid(), "bybit", MockBybitExchange.ApiKey, MockBybitExchange.ApiSecret),
            new InstrumentSymbol("BTCUSDT"),
            CancellationToken.None);

        rates.ShouldBe(new VenueFeeRatesInternal(MakerRate: 0.0008m, TakerRate: 0.001m));
    }

    [Test]
    public void ReadClientCannotWriteOrders()
    {
        var read = new BybitReadClient(
            new HttpClient(_harness.Exchange) { BaseAddress = new Uri("https://bybit.invalid/") },
            new BybitServerClock(new BybitOptions(), TimeProvider.System));
        var credential = new ExchangeCredentialSecretsInternal(Guid.NewGuid(), "bybit", MockBybitExchange.ApiKey, MockBybitExchange.ApiSecret);

        Should.Throw<ArgumentException>(() => read.Get(credential, "/v5/order/create", "category=spot", CancellationToken.None));
        Should.Throw<ArgumentException>(() => read.Public("/v5/account/wallet-balance", CancellationToken.None));
    }

    [Test]
    public void WithoutTradingTheVenueRegistersNoGatewayOrWriteClient()
    {
        ServiceProvider venue = new ServiceCollection().AddLogging().AddBybitVenue(new ConfigurationBuilder().Build()).BuildServiceProvider();
        ServiceProvider trading = new ServiceCollection().AddLogging().AddBybitVenue(new ConfigurationBuilder().Build()).AddBybitTrading().BuildServiceProvider();

        venue.GetServices<IOrderGateway>().ShouldBeEmpty();
        venue.GetService<BybitTradeClient>().ShouldBeNull();
        venue.GetServices<IVenueFeeRateReader>().ShouldNotBeEmpty();
        trading.GetServices<IOrderGateway>().Select(gateway => gateway.Venue.Value).ShouldBe(["bybit", "bybit-linear"]);
    }

    [Test]
    public async Task ReconcileReportsFoundOrdersWithTheirState()
    {
        string clientOrderId = BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId: 10).Value;
        _harness.Exchange.AddOrder(clientOrderId, "Buy", "Filled", cumulativeBase: 0.1m, cumulativeQuote: 10m, new Dictionary<string, decimal> { ["BTC"] = 0.0001m });
        _harness.Exchange.Executions.Add(clientOrderId);

        BotReconciliationInternal result = await Reconcile(
            allowForeign: false,
            Order(engineOrderId: 10, mayHaveReached: true),
            Order(engineOrderId: 11, mayHaveReached: false));

        result.Degradation.ShouldBeNull();
        result.Found.ShouldBe([new ReconciledOrderInternal(new ClientOrderId(clientOrderId), IsFinal: true)]);
        result.Updates.ShouldBe([ExternalOrderUpdate.Accept(orderId: 10), ExternalOrderUpdate.Final(orderId: 10, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m)]);
    }

    [Test]
    public async Task ReconcileLeavesAnOrderThatMayHaveBeenSentUnresolved()
    {
        BotReconciliationInternal result = await Reconcile(allowForeign: false, Order(engineOrderId: 12, mayHaveReached: true));

        result.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder);
    }

    [Test]
    public async Task ASubmitBybitNeverKnewIsRejectedAsAbsentOnlyAfterTheSettleDelayWithNoExecutionAndAnEqualBalance()
    {
        _harness.Exchange.WalletBalance = Wallet(baseBalance: "0.5");
        ReconcileOrderInternal order = Unaccepted(engineOrderId: 20, placedHoursAgo: 30);

        BotReconciliationInternal result = await Reconcile(allowForeign: false, expectedBase: 0.5m, order);

        result.ShouldSatisfyAllConditions(
            () => result.Degradation.ShouldBeNull(),
            () => result.Updates.ShouldHaveSingleItem()
                .ShouldSatisfyAllConditions(
                    update => update.Kind.ShouldBe(ExternalOrderUpdateKind.Rejected),
                    update => update.Reason.ShouldBe(RejectionReason.AbsenceProven),
                    update => update.OwnerConfirmedAbsent.ShouldBeTrue()),
            () => result.Found.ShouldHaveSingleItem().IsFinal.ShouldBeTrue(),
            () => result.Found.Single()
                .AbsenceProof!.Checks.ShouldBe(
                [
                    "NotFoundInOpenAndHistoricalOrders",
                    "SettleDelayPassed",
                    "NoExecutionOfOrder",
                    "BaseBalanceMatchesEngine"
                ]));
        _harness.Exchange.Requests.ShouldContain(request => request.Path == "/v5/execution/list");
    }

    [TestCase(arg1: 23, "SettleDelayNotPassed")]
    [TestCase(24 * 7, "OrderTooOld")]
    public async Task ASubmitTooYoungOrTooOldIsLeftToTheOwner(int placedHoursAgo, string failed)
    {
        await StaysUnresolved(Unaccepted(engineOrderId: 21, placedHoursAgo), failed);
    }

    [Test]
    public async Task AnOrderTheEngineSawAcceptedOrOneWithoutAKnownPlaceStartIsNeverClosed()
    {
        await StaysUnresolved(Unaccepted(engineOrderId: 22, placedHoursAgo: 30) with { AcceptedByEngine = true }, "EngineAcceptedOrder");
        await StaysUnresolved(Unaccepted(engineOrderId: 23, placedHoursAgo: 30) with { PlaceStartedAt = null }, "PlaceStartUnknown");
    }

    [Test]
    public async Task AnExecutionOfTheOrderOrAnUnequalBalanceOrAnUnreadableAnswerLeavesItUnresolved()
    {
        ReconcileOrderInternal order = Unaccepted(engineOrderId: 24, placedHoursAgo: 30);
        _harness.Exchange.Executions.Add(order.ClientOrderId.Value);
        await StaysUnresolved(order, "ExecutionFound");

        _harness.Exchange.Executions.Clear();
        _harness.Exchange.WalletBalance = Wallet(baseBalance: "0.7");
        await StaysUnresolved(order, "BalanceDiffers");

        _harness.Exchange.WalletBalance = null;
        await StaysUnresolved(order, "ReadFailed");

        _harness.Exchange.WalletBalance = Wallet(baseBalance: "0.5");
        (await Reconcile(allowForeign: false, expectedBase: null, order)).DegradationDetail.ShouldBe("AutoCheck:ExpectedPositionUnknown");
    }

    [Test]
    public async Task TheDryCheckIsTrueOnlyForAnOrderItWouldClose()
    {
        _harness.Exchange.WalletBalance = Wallet(baseBalance: "0.5");

        bool closable = await _harness.Gateway.CanProveAbsence(
            new BotReconcileInternal(_harness.Bot.Id, AllowForeignOrders: false, [Unaccepted(engineOrderId: 25, placedHoursAgo: 30)], ExpectedBase: 0.5m),
            CancellationToken.None);
        bool tooYoung = await _harness.Gateway.CanProveAbsence(
            new BotReconcileInternal(_harness.Bot.Id, AllowForeignOrders: false, [Unaccepted(engineOrderId: 26, placedHoursAgo: 1)], ExpectedBase: 0.5m),
            CancellationToken.None);
        bool nothing = await _harness.Gateway.CanProveAbsence(
            new BotReconcileInternal(_harness.Bot.Id, AllowForeignOrders: false, [], ExpectedBase: 0.5m),
            CancellationToken.None);

        closable.ShouldBeTrue();
        tooYoung.ShouldBeFalse();
        nothing.ShouldBeFalse();
    }

    [TestCase(arg1: true, arg2: false)]
    [TestCase(arg1: false, arg2: true)]
    public async Task ReconcileRefusesAKeyThatCannotTradeOrCanWithdraw(bool readOnly, bool canWithdraw)
    {
        _harness.Exchange.KeyReadOnly = readOnly;
        _harness.Exchange.KeyCanWithdraw = canWithdraw;

        BotReconciliationInternal result = await Reconcile(allowForeign: false);

        result.Degradation.ShouldBe(BotStatusReason.KeyPermissions);
        _harness.Exchange.Requests.ShouldNotContain(request => request.Path == "/v5/execution/list");
    }

    [Test]
    public async Task ForeignOrderOnTheSymbolDegradesUnlessAllowed()
    {
        _harness.Exchange.AddOrder("pf-manual-order", "Buy", "New", cumulativeBase: 0m, cumulativeQuote: 0m, []);
        _harness.Exchange.Executions.Add(BybitClientOrderIds.For(BotId.New(), engineOrderId: 1).Value);

        (await Reconcile(allowForeign: false)).Degradation.ShouldBe(BotStatusReason.ForeignOrder);
        (await Reconcile(allowForeign: true)).Degradation.ShouldBeNull();
    }

    [Test]
    public async Task ReconcileThatCannotReadTheVenueThrows()
    {
        _harness.Exchange.ListUnavailable = true;

        await Should.ThrowAsync<BybitOrderUnresolvedException>(() => Reconcile(allowForeign: false));
    }

    private Task<BotReconciliationInternal> Reconcile(bool allowForeign, params ReconcileOrderInternal[] orders)
    {
        return _harness.Gateway.Reconcile(new BotReconcileInternal(_harness.Bot.Id, allowForeign, orders), CancellationToken.None);
    }

    private Task<BotReconciliationInternal> Reconcile(bool allowForeign, decimal? expectedBase, params ReconcileOrderInternal[] orders)
    {
        return _harness.Gateway.Reconcile(new BotReconcileInternal(_harness.Bot.Id, allowForeign, orders, expectedBase), CancellationToken.None);
    }

    private async Task StaysUnresolved(ReconcileOrderInternal order, string failed)
    {
        BotReconciliationInternal result = await Reconcile(allowForeign: false, expectedBase: 0.5m, order);

        result.ShouldSatisfyAllConditions(
            () => result.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder),
            () => result.DegradationDetail.ShouldBe($"AutoCheck:{failed}"),
            () => result.Updates.ShouldBeEmpty(),
            () => result.Found.ShouldBeEmpty());
    }

    private ReconcileOrderInternal Unaccepted(long engineOrderId, int placedHoursAgo)
    {
        return new ReconcileOrderInternal(
            engineOrderId,
            BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId),
            MayHaveReachedVenue: true,
            OwnerConfirmedAbsent: false,
            ExecutedBase: 0m,
            _harness.Time.GetUtcNow().AddHours(-placedHoursAgo),
            AcceptedByEngine: false);
    }

    private static string Wallet(string baseBalance)
    {
        return $$"""{"list":[{"accountType":"UNIFIED","coin":[{"coin":"USDT","walletBalance":"1000"},{"coin":"BTC","walletBalance":"{{baseBalance}}"}]}]}""";
    }

    private ReconcileOrderInternal Order(long engineOrderId, bool mayHaveReached)
    {
        return new ReconcileOrderInternal(engineOrderId, BybitClientOrderIds.For(_harness.Bot.Id, engineOrderId), mayHaveReached);
    }

    private Task<ExternalOrderUpdate?> Submit(OrderSnapshot order, bool mustLookUp = false)
    {
        var submit = new OrderSubmitInternal(_harness.Bot.Id, BybitClientOrderIds.For(_harness.Bot.Id, order.Id), order, mustLookUp);

        return _harness.Gateway.Submit(
            submit,
            _ =>
            {
                _placeStarts++;

                return Task.CompletedTask;
            },
            CancellationToken.None);
    }
}