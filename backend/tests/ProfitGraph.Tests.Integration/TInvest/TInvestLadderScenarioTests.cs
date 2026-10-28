using System.Globalization;

using Dapper;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.CreateExchangeCredential;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotWaitingOrders;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Infrastructure.Bots.Runtime;
using ProfitGraph.Tests.Integration.Bots;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Unit.Venues.TInvest;
using ProfitGraph.Venues.TInvest;
using ProfitGraph.Venues.TInvest.Accounts;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using OrderType = Tinkoff.InvestApi.V1.OrderType;

namespace ProfitGraph.Tests.Integration.TInvest;

/// <summary>
///     A Ladder bot on a MOEX share through the real T-Invest module against the in-process mock Invest API (ADR 0023,
///     T15): a Worker with the live role and trading enabled only in this test host, an invented token and account, daily
///     four-hour candles of an invented SBER, fast worker intervals. Nothing leaves the process; no real token, account or endpoint is
///     used.
/// </summary>
[NonParallelizable]
public sealed class TInvestLadderScenarioTests
{
    private static readonly string FakeMasterKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x31, count: 32).ToArray());

    private string _accountId = string.Empty;
    private ProfitGraphApiFactory _api = null!;
    private MockTInvestApi _invest = null!;
    private MockTInvestOrders _orders = null!;
    private Share _share = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _api.DisposeAsync();
    }

    [SetUp]
    public async Task CreateMock()
    {
        await RuntimeScenario.StopEveryBot();
        _invest = new MockTInvestApi();
        var market = new MockTInvestMarket(_invest);
        _accountId = "2" + Random.Shared.NextInt64(minValue: 100_000_000, maxValue: 999_999_999).ToString(CultureInfo.InvariantCulture);
        _orders = new MockTInvestOrders(_invest, _accountId);
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        _share = market.AddShare("SBER", lot: 10, increment: 0.01m, today.AddDays(-400));

        DateTimeOffset settled = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddHours(DateTime.UtcNow.Hour / 4 * 4).AddHours(-4);

        for (DateTimeOffset open = today.AddDays(-30); open <= settled; open = open.AddHours(4))
        {
            if (open.ToOffset(TimeSpan.FromHours(3)).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                market.AddCandle("SBER", open, close: 100m);
            }
        }
    }

    [Test]
    public async Task AnUnknownSubmitIsLookedUp_AFarBuyWaitsForTheBand_AndALateCommissionIsCorrected()
    {
        _orders.NextPostFailure = MockTInvestApi.Refusal(StatusCode.Unavailable, "connection reset");
        _orders.PlaceBeforeFailing = true;
        _orders.Band = (96m, 150m);
        using IHost worker = Worker();
        Bot bot = await RunningLadder(worker);
        await worker.StartAsync();

        await RuntimeScenario.Until(() => Task.FromResult(_orders.Posted.Count == 1), "the entry is placed");
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))?.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
            "the entry whose answer was lost is found by its request ID");
        _orders.Posted.Count.ShouldBe(1);
        PostOrderRequest entry = _orders.Posted.Single();
        entry.ShouldSatisfyAllConditions(
            () => entry.OrderType.ShouldBe(OrderType.Market),
            () => entry.Direction.ShouldBe(OrderDirection.Buy),
            () => entry.Quantity.ShouldBe(1));

        _orders.Fill(entry.OrderId, lots: 1, price: 100m, commission: 0m);
        await RuntimeScenario.Until(async () => (await Waiting(worker, bot)).Any(order => order.Code == OrderWaitingCode.PriceBand), "the averaging buy waits");
        BotOrderWaitingInternal band = (await Waiting(worker, bot)).Single(order => order.Code == OrderWaitingCode.PriceBand);
        band.ShouldSatisfyAllConditions(() => band.Low.ShouldBe(96m), () => band.High.ShouldBe(150m));
        await RuntimeScenario.Until(() => Task.FromResult(_orders.Posted.Count == 2), "the exit is placed");
        _orders.Posted[1].Direction.ShouldBe(OrderDirection.Sell);

        _orders.Commission(entry.OrderId, commission: 1m);
        await RuntimeScenario.Until(async () => (await Corrections(bot)).Count == 1, "the late commission is corrected");
        (await Corrections(bot)).ShouldBe([(1L, 0m, 1m)]);

        _orders.Band = (90m, 150m);
        await RuntimeScenario.Until(() => Task.FromResult(_orders.Posted.Count == 3), "the averaging buy leaves once the band reaches it");
        PostOrderRequest average = _orders.Posted[2];
        average.ShouldSatisfyAllConditions(
            () => average.Direction.ShouldBe(OrderDirection.Buy),
            () => average.OrderType.ShouldBe(OrderType.Limit),
            () => average.TimeInForce.ShouldBe(TimeInForceType.TimeInForceDay));
        await RuntimeScenario.Until(async () => (await Waiting(worker, bot)).Count == 0, "the wait ends");
        (await RuntimeScenario.Read(worker, bot.Id)).IsDegraded.ShouldBeFalse();
        await worker.StopAsync();
    }

    [Test]
    public async Task ADayOrderWithAPartialFillIsCarriedOver_AndARestartAtNightSendsNothing()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _orders.Schedule.Add(
            (
                DateOnly.FromDateTime(now.UtcDateTime),
                [("regular_trading_session_main", now.AddHours(-2), now.AddMinutes(3))]
            ));
        Bot bot;

        using (IHost first = Worker())
        {
            bot = await RunningLadder(first);
            await first.StartAsync();
            await RuntimeScenario.Until(() => Task.FromResult(_orders.Posted.Count == 1), "the entry is placed");
            _orders.BuyLotsFilledOnPost = 1;
            _orders.Fill(_orders.Posted[0].OrderId, lots: 1, price: 100m, commission: 0.5m);

            await RuntimeScenario.Until(
                async () => (await Waiting(first, bot)).Count(order => order.Code == OrderWaitingCode.CarryOver) == 2,
                "the exit and the partly filled averaging buy are carried over");
            PostOrderRequest average = _orders.Posted.Single(posted => posted is { Direction: OrderDirection.Buy, OrderType: OrderType.Limit });
            average.Quantity.ShouldBe(2);
            _orders.Cancelled.Select(cancel => cancel.OrderId).ShouldContain(average.OrderId);
            (await Fills(bot)).Count.ShouldBe(2);
            await first.StopAsync();
        }

        int posted = _orders.Posted.Count;
        _orders.TradingOpen = false;

        using IHost second = Worker();
        await second.StartAsync();
        await RuntimeScenario.Until(async () => (await RuntimeScenario.Read(second, bot.Id)).RuntimeStatus == BotRuntimeStatus.Running, "the bot runs again");
        await Task.Delay(TimeSpan.FromSeconds(1));

        _orders.Posted.Count.ShouldBe(posted);
        (await Waiting(second, bot)).Count(order => order.Code == OrderWaitingCode.CarryOver).ShouldBe(2);
        (await RuntimeScenario.Read(second, bot.Id)).IsDegraded.ShouldBeFalse();
        await second.StopAsync();
    }

    [Test]
    public async Task ALostOrderStreamSubscribesAgain_AndTheFillItMissedReachesTheRunningBot()
    {
        using IHost worker = Worker(new PrivateStreamReconnectPolicy { Delays = [TimeSpan.FromMilliseconds(200)], Budget = TimeSpan.FromSeconds(30) });
        Bot bot = await RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))?.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
            "the entry is placed and accepted");

        _orders.DropStreams();
        _orders.Fill(_orders.Posted.Single().OrderId, lots: 1, price: 100m, commission: 0.5m);

        await RuntimeScenario.Until(() => Task.FromResult(_orders.Subscriptions == 2), "the order state stream subscribes again");
        await RuntimeScenario.Until(async () => (await Fills(bot)).Count == 1, "the fill missed while the stream was lost is booked");
        await RuntimeScenario.Until(() => Task.FromResult(_orders.Posted.Count >= 2), "the ladder goes on after the fill");
        (await Fills(bot)).Count.ShouldBe(1);
        Bot stored = await RuntimeScenario.Read(worker, bot.Id);
        stored.ShouldSatisfyAllConditions(
            () => stored.RuntimeStatus.ShouldBe(BotRuntimeStatus.Running),
            () => stored.IsDegraded.ShouldBeFalse(),
            () => stored.RestartAttempts.ShouldBe(0));
        await worker.StopAsync();
    }

    [Test]
    public async Task AnOrderStreamLostForTheWholeBudgetFailsTheBotForARestart()
    {
        using IHost worker = Worker(new PrivateStreamReconnectPolicy { Delays = [TimeSpan.FromMilliseconds(100)], Budget = TimeSpan.FromSeconds(1) });
        Bot bot = await RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(() => Task.FromResult(_orders.Posted.Count == 1), "the entry is placed");

        _orders.StreamFailure = MockTInvestApi.Refusal(StatusCode.Unavailable, "connection reset");
        _orders.DropStreams();

        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Read(worker, bot.Id)).StatusReason == BotStatusReason.RuntimeFailure,
            "the bot fails once the stream stayed lost");
        await worker.StopAsync();
        Bot stored = await RuntimeScenario.Read(worker, bot.Id);
        stored.ShouldSatisfyAllConditions(() => stored.IsDegraded.ShouldBeTrue(), () => stored.NextRestartAt.ShouldNotBeNull());
        _orders.Subscriptions.ShouldBe(1);
    }

    private IHost Worker(PrivateStreamReconnectPolicy? reconnect = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Secrets:ActiveKeyId"] = "test-v1",
            ["Secrets:MasterKeys:test-v1"] = FakeMasterKey,
            ["Bots:Runtime:LeaseDuration"] = TimeSpan.FromSeconds(5).ToString(),
            ["Bots:Runtime:RenewInterval"] = TimeSpan.FromMilliseconds(500).ToString(),
            ["Bots:Runtime:PollingInterval"] = TimeSpan.FromMilliseconds(100).ToString(),
            ["Bots:Runtime:DispatchInterval"] = TimeSpan.FromMilliseconds(50).ToString(),
            ["Bots:Runtime:ReconcileRetryDelay"] = TimeSpan.FromMilliseconds(100).ToString(),
            ["Venues:TInvest:Enabled"] = "true",
            ["Venues:TInvest:Endpoint"] = "https://invest.invalid/",
            ["Venues:TInvest:WaitingRecheckInterval"] = TimeSpan.FromMilliseconds(200).ToString(),
            ["Venues:TInvest:CommissionRecheckInterval"] = TimeSpan.FromMilliseconds(200).ToString()
        };
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return BotTestHost.Create(
            services =>
            {
                services.AddBotRuntime();
                services.AddTInvestVenue(configuration);
                services.AddTInvestTrading();
                services.AddSingleton<ITInvestCallInvokers>(new MockTInvestInvokers(_invest));

                if (reconnect is not null)
                {
                    services.AddSingleton(reconnect);
                }
            },
            settings: settings);
    }

    /// <summary>
    ///     A Ladder bot on the invented SBER, daily, with an invented T-Invest token for the mock's account, that should run.
    /// </summary>
    private async Task<Bot> RunningLadder(IHost host)
    {
        var owner = UserId.New();

        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync(
                "INSERT INTO users (id, login, password_hash, role, status, created_at, updated_at) VALUES (@owner, 'tinvest-' || @owner, 'not-a-real-hash', 'User', 'Active', now(), now())",
                new { owner = owner.Value });
        }

        using IServiceScope scope = host.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        ExchangeCredentialInternal credential = await sender.Send(
            new CreateExchangeCredentialCommand(
                owner.Value,
                "tinvest",
                "Invented",
                ApiKey: null,
                ApiSecret: null,
                new Dictionary<string, string>
                    { [TInvestCredentialFields.Token] = "t.invented-" + Guid.NewGuid().ToString("N"), [TInvestCredentialFields.AccountId] = _accountId }));
        StrategyDefinition definition = LadderStrategyBlockFactory.Create(buyAmount: 2000m, allocationCap: 10000m, drop: 0.05m, rise: 0.05m);
        StrategyInternal strategy = await sender.Send(
            new CreateStrategyCommand(owner.Value, "T-Invest ladder", definition.ToInternal(), new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())));
        StrategyVersion version = (await scope.ServiceProvider.GetRequiredService<IStrategyRepository>()
            .GetVersion(new StrategyId(strategy.Id), new StrategyVersionNumber(strategy.CurrentVersion), CancellationToken.None))!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var bot = Bot.Create(
            BotId.New(),
            owner,
            new StrategyId(strategy.Id),
            version.Id,
            new ExchangeCredentialId(credential.Id),
            new CandleSeries(TInvestVenue.VenueId, new InstrumentSymbol("TQBR:SBER"), Timeframe.Parse("4h")),
            new BotParameters(definition.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue)),
            allocationCap: 10000m,
            new BotRiskLimits(maxOrderQuote: 3000m, dailyLossLimitQuote: 5000m),
            new BotFeeRates(limitRate: 0.0005m, marketRate: 0.0005m, now),
            now);
        bot.Start(version.Id, hasCheckpoint: false, tradingHalted: false, now);
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return bot;
    }

    private static async Task<IReadOnlyList<BotOrderWaitingInternal>> Waiting(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotWaitingOrdersQuery(bot.OwnerId.Value, bot.Id.Value));
    }

    private static async Task<IReadOnlyList<(long EngineOrderId, decimal PreviousFee, decimal CorrectedFee)>> Corrections(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return
        [
            .. await connection.QueryAsync<(long, decimal, decimal)>(
                "SELECT engine_order_id, previous_fee, corrected_fee FROM bot_fee_corrections WHERE bot_id = @id ORDER BY corrected_at",
                new { id = bot.Id.Value })
        ];
    }

    private static async Task<IReadOnlyList<long>> Fills(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return [.. await connection.QueryAsync<long>("SELECT engine_order_id FROM bot_fills WHERE bot_id = @id ORDER BY sequence", new { id = bot.Id.Value })];
    }
}