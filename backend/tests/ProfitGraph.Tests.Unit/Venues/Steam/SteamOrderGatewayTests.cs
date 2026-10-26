using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Confirmations;
using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.Orders;
using ProfitGraph.Venues.Steam.Protection;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     The Steam order gateway against the mock: routing, and reconciliation from the journal and its attributed history
///     (ADR 0022).
/// </summary>
public sealed class SteamOrderGatewayTests
{
    private const string Item = "Dreams & Nightmares Case";

    private static readonly Guid Bot = Guid.NewGuid();
    private static readonly SellerFeeSchedule Fees = new SellerFeeSchedule([0.05m, 0.10m], minimum: 0.01m);

    private SteamSessionHarness _harness = null!;
    private InMemorySteamOrderJournal _journal = null!;
    private InMemorySteamMarketHistoryStore _receipts = null!;
    private InMemorySteamMarketAttributions _attributions = null!;
    private SteamOrderUpdates _updates = null!;
    private RecordingSteamOrderWaiting _waiting = null!;
    private FakeSteamAccountStore _accounts = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new SteamSessionHarness
        {
            Options = new SteamOptions
            {
                Enabled = true,
                CommunityBaseAddress = MockSteam.Community,
                LoginBaseAddress = MockSteam.Login,
                ApiBaseAddress = MockSteam.Api,
                WriteSpacing = TimeSpan.Zero
            }
        };
        _journal = new InMemorySteamOrderJournal();
        _receipts = new InMemorySteamMarketHistoryStore();
        _attributions = new InMemorySteamMarketAttributions();
        _updates = new SteamOrderUpdates();
        _waiting = new RecordingSteamOrderWaiting();
        _accounts = new FakeSteamAccountStore();
    }

    [Test]
    public async Task AnAccountWithoutAReadyGuardMayNotTrade()
    {
        _accounts.Status = SteamGuardStatus.Checking;

        (await Gateway().Reconcile(Request(), CancellationToken.None)).Degradation.ShouldBe(BotStatusReason.KeyPermissions);
    }

    [Test]
    public async Task AnOpenProtectionHoldsTheBot()
    {
        await _harness.Store.Open(SteamSessionHarness.Credential, SteamProtectionReason.Captcha, _harness.Clock.GetUtcNow(), CancellationToken.None);

        (await Gateway().Reconcile(Request(), CancellationToken.None)).Degradation.ShouldBe(BotStatusReason.VenueAccountProtection);
    }

    [Test]
    public async Task AWalletInAnotherCurrencyMayNotTradeTheItem()
    {
        _harness.Steam.WalletInfo = _harness.Steam.WalletInfo.Replace("\"wallet_currency\":37", "\"wallet_currency\":1", StringComparison.Ordinal);

        (await Gateway().Reconcile(Request(), CancellationToken.None)).Degradation.ShouldBe(BotStatusReason.KeyPermissions);
    }

    [Test]
    public async Task AForeignBuyOrderOnTheItemIsRefusedUnlessAllowed()
    {
        _harness.Steam.BuyOrdersHtml = """
                                       <div id="myListings"><div class="market_listing_row" id="mybuyorder_7001">
                                       <a class="market_listing_item_name_link" href="https://community.steam.invalid/market/listings/730/Dreams%20%26%20Nightmares%20Case">Case</a>
                                       <span class="market_listing_price">1 @ 500,00₸</span></div>
                                       <div class="market_listing_row" id="mybuyorder_7002">
                                       <a class="market_listing_item_name_link" href="https://community.steam.invalid/market/listings/730/Other%20Case">Other</a></div></div>
                                       """;
        SteamOrderGateway gateway = Gateway();

        (await gateway.Reconcile(Request(), CancellationToken.None)).Degradation.ShouldBe(BotStatusReason.ForeignOrder);
        (await gateway.Reconcile(Request() with { AllowForeignOrders = true }, CancellationToken.None)).Degradation.ShouldBeNull();
    }

    [Test]
    public async Task ReconciliationTellsTheJournalsStateAndProvenFills()
    {
        SteamOrderGateway gateway = Gateway();
        OrderSnapshot order = Buy(quantity: 2);
        ClientOrderId id = gateway.ClientOrderIdFor(new BotId(Bot), engineOrderId: 1);
        (await gateway.Submit(new OrderSubmitInternal(new BotId(Bot), id, order, MustLookUpBeforeSubmit: false), _ => Task.CompletedTask, CancellationToken.None))
            .ShouldBeNull();
        var receipt = new SteamMarketReceipt(
            "4:701:9:1:0",
            SteamMarketReceiptKind.Bought,
            string.Empty,
            AppId: 730,
            Item,
            "2",
            "3001",
            Quantity: 1,
            BuyerPaysMinorUnits: 50000,
            SellerReceivesMinorUnits: 43478,
            DateTimeOffset.UnixEpoch);
        await _receipts.Append(SteamSessionHarness.Credential, expectedNext: null, [new SteamStoredReceipt(Position: 0, receipt)], CancellationToken.None);
        await _attributions.Attribute(
            SteamSessionHarness.Credential,
            [new SteamAttribution(receipt.Id, id.Value, Bot)],
            DateTimeOffset.UnixEpoch,
            CancellationToken.None);

        BotReconciliationInternal reconciliation = await gateway.Reconcile(
            Request(new ReconcileOrderInternal(EngineOrderId: 1, id, MayHaveReachedVenue: true)),
            CancellationToken.None);

        reconciliation.Degradation.ShouldBeNull();
        reconciliation.Found.ShouldBe([new ReconciledOrderInternal(id, IsFinal: false)]);
        reconciliation.Updates.ShouldBe([ExternalOrderUpdate.Accept(orderId: 1), ExternalOrderUpdate.Partial(orderId: 1, grossBase: 1m, grossQuote: 500m, fee: 0m)]);
    }

    [Test]
    public async Task AnOrderWhoseWriteIsUnclearIsUnresolved()
    {
        SteamOrderGateway gateway = Gateway();
        ClientOrderId id = gateway.ClientOrderIdFor(new BotId(Bot), engineOrderId: 1);
        await Should.ThrowAsync<InvalidOperationException>(() => gateway.Submit(
            new OrderSubmitInternal(new BotId(Bot), id, Buy(quantity: 1), MustLookUpBeforeSubmit: false),
            _ => throw new InvalidOperationException("The process stops here."),
            CancellationToken.None));

        (await gateway.Reconcile(Request(new ReconcileOrderInternal(EngineOrderId: 1, id, MayHaveReachedVenue: true)), CancellationToken.None))
            .Degradation.ShouldBe(BotStatusReason.UnresolvedOrder);
    }

    [Test]
    public async Task ABuyTheOwnerConfirmedAbsentEndsRejectedWhenSteamShowsNothingOfIt()
    {
        SteamOrderGateway gateway = Gateway();
        ClientOrderId id = await UnclearBuy(gateway);

        BotReconciliationInternal reconciliation = await gateway.Reconcile(Request(ConfirmedAbsent(id)) with { AllowForeignOrders = true }, CancellationToken.None);

        reconciliation.Degradation.ShouldBeNull();
        reconciliation.Found.ShouldBe([new ReconciledOrderInternal(id, IsFinal: true)]);
        ExternalOrderUpdate update = reconciliation.Updates.ShouldHaveSingleItem();
        update.Kind.ShouldBe(ExternalOrderUpdateKind.Rejected);
        update.Reason.ShouldBe(RejectionReason.OwnerConfirmedAbsent);
        update.OwnerConfirmedAbsent.ShouldBeTrue();
        SteamOrderJournalEntry entry = (await _journal.Get(id.Value, CancellationToken.None))!;
        entry.State.ShouldBe(SteamOrderJournalState.Done);
        entry.Outcome.ShouldBe(SteamOrderOutcome.Rejected);
        _harness.Steam.Count("/market/myhistory/render/").ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task ABuyOrderOnTheItemKeepsAConfirmedBuyUnresolved()
    {
        SteamOrderGateway gateway = Gateway();
        ClientOrderId id = await UnclearBuy(gateway);
        _harness.Steam.BuyOrdersHtml = """
                                       <div id="myListings"><div class="market_listing_row" id="mybuyorder_7001">
                                       <a class="market_listing_item_name_link" href="https://community.steam.invalid/market/listings/730/Dreams%20%26%20Nightmares%20Case">Case</a>
                                       <span class="market_listing_price">1 @ 500,00₸</span></div></div>
                                       """;

        BotReconciliationInternal reconciliation = await gateway.Reconcile(Request(ConfirmedAbsent(id)) with { AllowForeignOrders = true }, CancellationToken.None);

        reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder);
        (await _journal.Get(id.Value, CancellationToken.None))!.State.ShouldNotBe(SteamOrderJournalState.Done);
    }

    [Test]
    public async Task APurchaseOfTheItemSinceThePlaceRequestKeepsAConfirmedBuyUnresolved()
    {
        SteamOrderGateway gateway = Gateway();
        ClientOrderId id = await UnclearBuy(gateway);
        _harness.Steam.History.Add(
            new MockSteamHistoryEvent(Type: 4, "5001", _harness.Clock.GetUtcNow().ToUnixTimeSeconds(), "205001", "95001", "6001", Paid: 50000, Fee: 0));

        BotReconciliationInternal reconciliation = await gateway.Reconcile(Request(ConfirmedAbsent(id)) with { AllowForeignOrders = true }, CancellationToken.None);

        reconciliation.Degradation.ShouldBe(BotStatusReason.UnresolvedOrder);
        (await _journal.Get(id.Value, CancellationToken.None))!.State.ShouldNotBe(SteamOrderJournalState.Done);
    }

    [Test]
    public async Task AnOrderTheJournalNeverSawCancelsLocally()
    {
        SteamOrderGateway gateway = Gateway();

        await gateway.Cancel(new BotId(Bot), gateway.ClientOrderIdFor(new BotId(Bot), engineOrderId: 9), CancellationToken.None);

        (await Published()).ShouldBe([ExternalOrderUpdate.ConfirmCancel(orderId: 9)]);
        _harness.Steam.Count("/market/cancelbuyorder/").ShouldBe(0);
    }

    [Test]
    public async Task TheWorkerPlacesABuyOnceItsSlotFrees()
    {
        SteamOrderGateway gateway = Gateway();
        ClientOrderId first = gateway.ClientOrderIdFor(new BotId(Bot), engineOrderId: 1);
        ClientOrderId second = gateway.ClientOrderIdFor(new BotId(Bot), engineOrderId: 2);
        await gateway.Submit(
            new OrderSubmitInternal(new BotId(Bot), first, Buy(quantity: 1), MustLookUpBeforeSubmit: false),
            _ => Task.CompletedTask,
            CancellationToken.None);
        await gateway.Submit(
            new OrderSubmitInternal(new BotId(Bot), second, Buy(quantity: 1, id: 2), MustLookUpBeforeSubmit: false),
            _ => Task.CompletedTask,
            CancellationToken.None);
        await gateway.Cancel(new BotId(Bot), first, CancellationToken.None);

        await Worker().Round(CancellationToken.None);

        _harness.Steam.Count("/market/createbuyorder/").ShouldBe(2);
        (await _journal.Get(second.Value, CancellationToken.None))!.State.ShouldBe(SteamOrderJournalState.Active);
    }

    private static OrderSnapshot Buy(int quantity, long id = 1)
    {
        return new OrderSnapshot(
            id,
            Tag: null,
            OrderSide.Buy,
            OrderType.Limit,
            quantity,
            LimitPrice: 500m,
            QuoteBudget: null,
            quantity * 500m,
            "node",
            EligibleFromCandleIndex: 0,
            OrderStatus.Active);
    }

    private static ReconcileOrderInternal ConfirmedAbsent(ClientOrderId id)
    {
        return new ReconcileOrderInternal(EngineOrderId: 1, id, MayHaveReachedVenue: true, OwnerConfirmedAbsent: true);
    }

    /// <summary>
    ///     A buy whose place request may have left without an answer: the process stops right after recording it.
    /// </summary>
    private static async Task<ClientOrderId> UnclearBuy(SteamOrderGateway gateway)
    {
        ClientOrderId id = gateway.ClientOrderIdFor(new BotId(Bot), engineOrderId: 1);
        await Should.ThrowAsync<InvalidOperationException>(() => gateway.Submit(
            new OrderSubmitInternal(new BotId(Bot), id, Buy(quantity: 1), MustLookUpBeforeSubmit: false),
            _ => throw new InvalidOperationException("The process stops here."),
            CancellationToken.None));

        return id;
    }

    private static BotReconcileInternal Request(params ReconcileOrderInternal[] orders)
    {
        return new BotReconcileInternal(new BotId(Bot), AllowForeignOrders: false, orders);
    }

    private static SteamBotAccount Account()
    {
        SteamInstrument.TryParse($"KZT:730:{Item}", out SteamInstrument? instrument).ShouldBeTrue();

        return new SteamBotAccount(Bot, SteamSessionHarness.Owner, SteamSessionHarness.Credential, instrument, Fees);
    }

    private SteamBuyOrders Buys()
    {
        IOptions<SteamOptions> options = Options.Create(_harness.Options);

        return new SteamBuyOrders(_journal, _harness.Sessions(), new SteamBuyOrderClient(options), _updates, new FakeSteamBotAccounts(Account), _waiting, _harness.Clock);
    }

    private SteamSells Sells()
    {
        IOptions<SteamOptions> options = Options.Create(_harness.Options);

        return new SteamSells(
            _journal,
            new FakeSteamOwnedAssets(),
            _harness.Sessions(),
            new SteamListingClient(options),
            new SteamSellSettlement(_journal, _updates, _waiting, _harness.Clock),
            _updates,
            _waiting,
            _harness.Clock);
    }

    private SteamOrderGateway Gateway()
    {
        var scopes = new ServiceCollection().AddSingleton<ISteamAccountStore>(_accounts).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new SteamOrderGateway(
            new FakeSteamBotAccounts(Account),
            _journal,
            _receipts,
            _attributions,
            Buys(),
            Sells(),
            _updates,
            _harness.MarketData(),
            _harness.Protection(),
            scopes,
            new SteamMarketHistory(_receipts, Options.Create(_harness.Options)),
            _harness.Sessions(),
            _harness.Clock);
    }

    private SteamOrderWorker Worker()
    {
        IOptions<SteamOptions> options = Options.Create(_harness.Options);
        SteamBuyOrders buys = Buys();
        var settlement = new SteamSellSettlement(_journal, _updates, _waiting, _harness.Clock);
        var executions = new SteamExecutions(
            _journal,
            _receipts,
            _attributions,
            new SteamMarketHistory(_receipts, options),
            _harness.Sessions(),
            new SteamBuyOrderClient(options),
            _updates,
            _harness.Clock);

        return new SteamOrderWorker(
            _journal,
            new FakeSteamBotAccounts(Account),
            buys,
            Sells(),
            new SteamConfirmations(_journal, _harness.Sessions(), new SteamConfirmationClient(_harness.Scopes()), buys, settlement, _waiting, _harness.Clock),
            executions,
            options,
            _harness.Clock,
            NullLogger<SteamOrderWorker>.Instance);
    }

    private async Task<IReadOnlyList<ExternalOrderUpdate>> Published()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var seen = new List<ExternalOrderUpdate>();

        try
        {
            await foreach (ExternalOrderUpdate update in _updates.Read(Bot, stop.Token))
            {
                seen.Add(update);
            }
        }
        catch (OperationCanceledException)
        {
            // Everything published so far has been read.
        }

        return seen;
    }
}