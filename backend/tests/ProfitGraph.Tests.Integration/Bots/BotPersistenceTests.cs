using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Bots, leases, checkpoints, intents and the halt round-trip through PostgreSQL unchanged.
/// </summary>
public sealed class BotPersistenceTests
{
    private ProfitGraphApiFactory _api = null!;
    private IHost _host = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
        _host = BotTestHost.Create();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _host.Dispose();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task BotRoundTripsWithEveryField()
    {
        Bot bot = await BotSeed.NewBot();
        bot.Start(bot.StrategyVersionId, hasCheckpoint: false, tradingHalted: false, BotSeed.Now);
        bot.RecordLeaseAcquired(BotSeed.Now);
        bot.MarkStarting(BotSeed.Now);
        bot.MarkRunning(BotSeed.Now);
        bot.RecordRuntimeFailure(BotSeed.Now);
        bot.RecordRun(BotSeed.Now);
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 99.5m, BotSeed.Now);
        bot.RecordDayEquity(new DateOnly(year: 2026, month: 10, day: 25), equity: 99.25m, BotSeed.Now);
        bot.RejectStrategyVersion(StrategyVersionId.New(), BotSeed.Now);
        bot.MarkFeedStale(stale: true, BotSeed.Now);

        await InTransaction(services => services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None));
        Bot? stored = await Read(services => services.GetRequiredService<IBotRepository>().Get(bot.Id, CancellationToken.None));

        stored.ShouldNotBeNull().ToSnapshot().ShouldBe(bot.ToSnapshot());
    }

    [Test]
    public async Task UpdatedBotIsListedForItsOwnerAndAsRunning()
    {
        Bot bot = await BotSeed.NewBot();
        await InTransaction(services => services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None));

        await InTransaction(async services =>
        {
            var bots = services.GetRequiredService<IBotRepository>();
            Bot locked = (await bots.GetForUpdate(bot.Id, CancellationToken.None)).ShouldNotBeNull();
            locked.Start(locked.StrategyVersionId, hasCheckpoint: false, tradingHalted: false, BotSeed.Now);
            await bots.Update(locked, CancellationToken.None);
        });

        IReadOnlyList<Bot> owned = await Read(services => services.GetRequiredService<IBotRepository>().ListByOwner(bot.OwnerId, CancellationToken.None));
        IReadOnlyList<BotId> running = await Read(services => services.GetRequiredService<IBotRepository>().ListDesiredRunning(CancellationToken.None));
        owned.Single().DesiredState.ShouldBe(BotDesiredState.Running);
        running.ShouldContain(bot.Id);
    }

    [Test]
    public async Task BotIsLockedOnlyInsideATransaction()
    {
        Bot bot = await BotSeed.NewBot();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Read(services => services.GetRequiredService<IBotRepository>().GetForUpdate(bot.Id, CancellationToken.None)));
    }

    [Test]
    public async Task LeaseRoundTripsAndTakesOver()
    {
        Bot bot = await BotSeed.NewBot();
        var first = LeaseHolderId.New();
        var second = LeaseHolderId.New();
        await InTransaction(async services =>
        {
            await services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);
            await services.GetRequiredService<IBotLeaseRepository>().Add(BotLease.Acquire(bot.Id, first, BotSeed.Now, TimeSpan.FromSeconds(30)), CancellationToken.None);
        });

        await InTransaction(async services =>
        {
            var leases = services.GetRequiredService<IBotLeaseRepository>();
            BotLease lease = (await leases.GetForUpdate(bot.Id, CancellationToken.None)).ShouldNotBeNull();
            lease.TakeOver(second, BotSeed.Now.AddSeconds(30), TimeSpan.FromSeconds(30));
            await leases.Update(lease, CancellationToken.None);
        });

        BotLease stored = (await InTransaction(services => services.GetRequiredService<IBotLeaseRepository>().GetForUpdate(bot.Id, CancellationToken.None)))
            .ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.HolderId.ShouldBe(second),
            () => stored.Epoch.ShouldBe(2),
            () => stored.ExpiresAt.ShouldBe(BotSeed.Now.AddSeconds(60)));
    }

    [Test]
    public async Task CheckpointRoundTripsThroughJson()
    {
        Bot bot = await BotSeed.NewBot();
        GraphEngineCheckpoint engine = BotSeed.Checkpoint(nextOrderId: 2, BotSeed.MarketBuy(id: 1));
        await InTransaction(async services =>
        {
            await services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);
            await services.GetRequiredService<IBotCheckpointRepository>().Save(new BotCheckpoint(bot.Id, engine, bot.Parameters, BotSeed.Now), CancellationToken.None);
        });

        BotCheckpoint stored = (await Read(services => services.GetRequiredService<IBotCheckpointRepository>().Get(bot.Id, CancellationToken.None))).ShouldNotBeNull();

        stored.ShouldSatisfyAllConditions(
            () => stored.AppliedParameters.ShouldBe(bot.Parameters),
            () => stored.Engine.Account.Orders.ShouldBe(engine.Account.Orders),
            () => stored.Engine.PendingSubmissionAttempts.ShouldBe(engine.PendingSubmissionAttempts),
            () => stored.Engine.LastClosedCandle.ShouldBe(engine.LastClosedCandle),
            () => stored.Engine.Account.QuoteReserved.ShouldBe(10m),
            () => stored.Engine.GraphVersionId.ShouldBe("v1"));
    }

    [Test]
    public async Task IntentRoundTripsAndTheSameKindForAnOrderIsStoredOnce()
    {
        Bot bot = await BotSeed.NewBot();
        OrderIntent submit = OrderIntent.Submit(OrderIntentId.New(), bot.Id, BotSeed.MarketBuy(id: 3), new ClientOrderId("pg-3"), BotSeed.Now);
        OrderIntent duplicate = OrderIntent.Submit(OrderIntentId.New(), bot.Id, BotSeed.MarketBuy(id: 3), new ClientOrderId("pg-3"), BotSeed.Now);
        OrderIntent cancel = OrderIntent.Cancel(OrderIntentId.New(), bot.Id, engineOrderId: 3, new ClientOrderId("pg-3"), fromKill: true, BotSeed.Now);
        bool[] added = [];

        await InTransaction(async services =>
        {
            await services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);
            var intents = services.GetRequiredService<IOrderIntentRepository>();
            added =
            [
                await intents.TryAdd(submit, CancellationToken.None), await intents.TryAdd(duplicate, CancellationToken.None),
                await intents.TryAdd(cancel, CancellationToken.None)
            ];
        });

        await InTransaction(async services =>
        {
            var intents = services.GetRequiredService<IOrderIntentRepository>();
            OrderIntent locked = (await intents.GetForUpdate(submit.Id, CancellationToken.None)).ShouldNotBeNull();
            locked.Claim(BotSeed.Now);
            locked.Reject(new OrderIntentRejection(RejectionReason.RiskLimit, "Order exceeds the per-order limit."), BotSeed.Now);
            await intents.Update(locked, CancellationToken.None);
        });

        IReadOnlyList<OrderIntent> stored = await Read(services => services.GetRequiredService<IOrderIntentRepository>().ListByBot(bot.Id, CancellationToken.None));
        added.ShouldBe([true, false, true]);
        stored.Select(intent => intent.Id).ShouldBe([submit.Id, cancel.Id]);
        stored[0]
            .ShouldSatisfyAllConditions(
                () => stored[0].Status.ShouldBe(OrderIntentStatus.Rejected),
                () => stored[0].Rejection.ShouldBe(new OrderIntentRejection(RejectionReason.RiskLimit, "Order exceeds the per-order limit.")),
                () => stored[0].Order.ShouldBe(BotSeed.MarketBuy(id: 3)));
        stored[1].ToSnapshot().ShouldBe(cancel.ToSnapshot());
    }

    [Test]
    public async Task AnIntentTheGatewayClosedOnItsOwnEvidenceRoundTripsWithNoConfirmingUser()
    {
        Bot bot = await BotSeed.NewBot();
        OrderIntent submit = OrderIntent.Submit(OrderIntentId.New(), bot.Id, BotSeed.MarketBuy(id: 4), new ClientOrderId("pg-4"), BotSeed.Now);
        await InTransaction(async services =>
        {
            await services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);
            await services.GetRequiredService<IOrderIntentRepository>().TryAdd(submit, CancellationToken.None);
        });
        await InTransaction(async services =>
        {
            var intents = services.GetRequiredService<IOrderIntentRepository>();
            OrderIntent locked = (await intents.GetForUpdate(submit.Id, CancellationToken.None)).ShouldNotBeNull();
            locked.Claim(BotSeed.Now);
            locked.MarkPlaceStarted(BotSeed.Now);
            locked.MarkSent(BotSeed.Now);
            locked.ConfirmExpiredAutomatically(["NotFoundByRequestId", "TradingDayEnded"], BotSeed.Now);
            await intents.Update(locked, CancellationToken.None);
        });

        OrderIntent stored = (await Read(services => services.GetRequiredService<IOrderIntentRepository>().ListByBot(bot.Id, CancellationToken.None))).Single();

        stored.ShouldSatisfyAllConditions(
            () => stored.ConfirmedAbsentAt.ShouldBe(BotSeed.Now),
            () => stored.ConfirmedAbsentBy.ShouldBeNull(),
            () => stored.ConfirmedAbsentAutomatically.ShouldBeTrue(),
            () => stored.Status.ShouldBe(OrderIntentStatus.Sent));
    }

    [Test]
    public async Task OnlyBotsThatShouldRunAndStoppedOnAnUnresolvedOrderAreListedForTheRecheck()
    {
        Bot unresolved = await DegradedBot(BotStatusReason.UnresolvedOrder);
        Bot foreign = await DegradedBot(BotStatusReason.ForeignOrder);
        Bot ownerStopped = await DegradedBot(BotStatusReason.UnresolvedOrder, stop: true);

        IReadOnlyList<BotId> listed = await Read(services => services.GetRequiredService<IBotRepository>().ListDegradedOnUnresolvedOrder(CancellationToken.None));

        listed.ShouldContain(unresolved.Id);
        listed.ShouldNotContain(foreign.Id);
        listed.ShouldNotContain(ownerStopped.Id);
    }

    [Test]
    public async Task EveryBotOfAVenueIsListedWhateverItsState_AndNoBotOfAnotherVenue()
    {
        Bot unresolved = await DegradedBot(BotStatusReason.UnresolvedOrder);
        Bot ownerStopped = await DegradedBot(BotStatusReason.UnresolvedOrder, stop: true);

        IReadOnlyList<Bot> listed = await Read(services => services.GetRequiredService<IBotRepository>().ListOnVenue(new VenueId("bybit"), CancellationToken.None));
        IReadOnlyList<Bot> elsewhere = await Read(services => services.GetRequiredService<IBotRepository>().ListOnVenue(new VenueId("tinvest"), CancellationToken.None));

        listed.Select(bot => bot.Id).ShouldContain(unresolved.Id);
        listed.Select(bot => bot.Id).ShouldContain(ownerStopped.Id);
        listed.ShouldAllBe(bot => bot.Series.Venue == new VenueId("bybit"));
        elsewhere.ShouldBeEmpty();
    }

    [Test]
    public async Task HaltIsASingleStoredRow()
    {
        await InTransaction(async services =>
        {
            var halts = services.GetRequiredService<ITradingHaltRepository>();
            TradingHalt halt = await halts.GetForUpdate(CancellationToken.None);
            halt.Halt(BotSeed.Now);
            await halts.Update(halt, CancellationToken.None);
        });

        TradingHalt stored = await Read(services => services.GetRequiredService<ITradingHaltRepository>().Get(CancellationToken.None));
        await InTransaction(async services =>
        {
            var halts = services.GetRequiredService<ITradingHaltRepository>();
            TradingHalt halt = await halts.GetForUpdate(CancellationToken.None);
            halt.Resume(BotSeed.Now);
            await halts.Update(halt, CancellationToken.None);
        });

        stored.IsHalted.ShouldBeTrue();
        stored.UpdatedAt.ShouldBe(BotSeed.Now);
    }

    private async Task<Bot> DegradedBot(BotStatusReason reason, bool stop = false)
    {
        Bot bot = await BotSeed.NewBot();
        bot.Start(bot.StrategyVersionId, hasCheckpoint: false, tradingHalted: false, BotSeed.Now);
        bot.RecordLeaseAcquired(BotSeed.Now);
        bot.MarkStarting(BotSeed.Now);
        bot.MarkRunning(BotSeed.Now);
        bot.Degrade(reason, BotSeed.Now);

        if (stop)
        {
            bot.Stop(BotSeed.Now);
        }

        await InTransaction(services => services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None));

        return bot;
    }

    private Task InTransaction(Func<IServiceProvider, Task> action)
    {
        return InTransaction(async services =>
        {
            await action(services);

            return true;
        });
    }

    private async Task<T> InTransaction<T>(Func<IServiceProvider, Task<T>> action)
    {
        using IServiceScope scope = _host.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>().ExecuteAsync(() => action(scope.ServiceProvider), CancellationToken.None);
    }

    private async Task<T> Read<T>(Func<IServiceProvider, Task<T>> action)
    {
        using IServiceScope scope = _host.Services.CreateScope();

        return await action(scope.ServiceProvider);
    }
}