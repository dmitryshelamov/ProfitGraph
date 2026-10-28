using Dapper;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Fills;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     A root commits only for the lease holder of the current epoch (ADR 0016); a fenced writer leaves no trace.
/// </summary>
public sealed class BotRootCommitterTests
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private readonly TestClock _clock = new TestClock { Now = BotSeed.Now };
    private ProfitGraphApiFactory _api = null!;
    private IHost _host = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
        _host = BotTestHost.Create(clock: _clock);
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _host.Dispose();
        await _api.DisposeAsync();
    }

    [SetUp]
    public void ResetClock()
    {
        _clock.Now = BotSeed.Now;
    }

    [Test]
    public async Task HolderCommitsCheckpointIntentsAndBotTogether()
    {
        (Bot bot, LeaseHolderId holder) = await LeasedBot();
        OrderIntent intent = OrderIntent.Submit(OrderIntentId.New(), bot.Id, BotSeed.MarketBuy(id: 1), new ClientOrderId("pg-1"), BotSeed.Now);

        await Commit(
            new BotRootCommit(bot.Id, holder, Epoch: 1, BotSeed.Checkpoint(nextOrderId: 2, BotSeed.MarketBuy(id: 1)), bot.Parameters, [intent]),
            stored => stored.ResetRestartStreak(_clock.Now));

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<bool>("SELECT has_run FROM bots WHERE id = @id", new { id = bot.Id.Value })).ShouldBeTrue();
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bot_checkpoints WHERE bot_id = @id", new { id = bot.Id.Value })).ShouldBe(1);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bot_order_intents WHERE bot_id = @id", new { id = bot.Id.Value })).ShouldBe(1);
    }

    [Test]
    public async Task SavingTheSameRootAgainAddsNoSecondIntent()
    {
        (Bot bot, LeaseHolderId holder) = await LeasedBot();
        var commit = new BotRootCommit(
            bot.Id,
            holder,
            Epoch: 1,
            BotSeed.Checkpoint(nextOrderId: 2, BotSeed.MarketBuy(id: 1)),
            bot.Parameters,
            [OrderIntent.Submit(OrderIntentId.New(), bot.Id, BotSeed.MarketBuy(id: 1), new ClientOrderId("pg-1"), BotSeed.Now)]);

        await Commit(commit);
        await Commit(commit with { Intents = [OrderIntent.Submit(OrderIntentId.New(), bot.Id, BotSeed.MarketBuy(id: 1), new ClientOrderId("pg-1"), BotSeed.Now)] });

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bot_order_intents WHERE bot_id = @id", new { id = bot.Id.Value })).ShouldBe(1);
    }

    [Test]
    public async Task ExpiredHolderIsFencedAndWritesNothing()
    {
        (Bot bot, LeaseHolderId holder) = await LeasedBot();
        _clock.Now = BotSeed.Now + LeaseDuration;

        await Should.ThrowAsync<BotLeaseLostException>(() => Commit(Root(bot, holder, epoch: 1)));

        await AssertNothingCommitted(bot);
    }

    [Test]
    public async Task TakeOverFencesTheFormerHolder()
    {
        (Bot bot, LeaseHolderId first) = await LeasedBot();
        var second = LeaseHolderId.New();
        _clock.Now = BotSeed.Now + LeaseDuration;
        await InTransaction(async services =>
        {
            var leases = services.GetRequiredService<IBotLeaseRepository>();
            BotLease lease = (await leases.GetForUpdate(bot.Id, CancellationToken.None)).ShouldNotBeNull();
            lease.TakeOver(second, _clock.Now, LeaseDuration);
            await leases.Update(lease, CancellationToken.None);
        });

        await Should.ThrowAsync<BotLeaseLostException>(() => Commit(Root(bot, first, epoch: 1)));
        await Should.ThrowAsync<BotLeaseLostException>(() => Commit(Root(bot, second, epoch: 1)));
        await AssertNothingCommitted(bot);
        await Commit(Root(bot, second, epoch: 2));
    }

    [Test]
    public async Task FencedRootLeavesNoFillOrPositionCycle()
    {
        (Bot bot, LeaseHolderId holder) = await LeasedBot();
        _clock.Now = BotSeed.Now + LeaseDuration;
        BotFill fill = BotFill.Record(
            BotFillId.New(),
            bot.Id,
            engineOrderId: 1,
            OrderSide.Buy,
            "entry",
            grossBase: 0.1m,
            grossQuote: 10m,
            fee: 0m,
            cumulativeGrossBase: 0.1m,
            BotSeed.Now);

        await Should.ThrowAsync<BotLeaseLostException>(() => Commit(
            Root(bot, holder, epoch: 1) with { Fills = [fill], Position = new BotRootPosition(BotPositionChange.Opened, BotSeed.Now, RealizedPnl: 0m) }));

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bot_fills WHERE bot_id = @id", new { id = bot.Id.Value })).ShouldBe(0);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bot_position_cycles WHERE bot_id = @id", new { id = bot.Id.Value })).ShouldBe(0);
        (await connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM outbox_messages WHERE type IN ('BotFillRecorded', 'BotPositionOpened') AND payload->>'BotId' = @id",
                new { id = bot.Id.Value.ToString() }))
            .ShouldBe(0);
    }

    [Test]
    public async Task OnlyTheCurrentHolderAndEpochCommitAmongConcurrentWriters()
    {
        (Bot bot, LeaseHolderId first) = await LeasedBot();
        var second = LeaseHolderId.New();

        Task[] attempts =
        [
            Commit(Root(bot, first, epoch: 1)),
            Commit(Root(bot, second, epoch: 1)),
            Commit(Root(bot, first, epoch: 2))
        ];
        Exception?[] outcomes = await Task.WhenAll(
            attempts.Select(async attempt =>
            {
                try
                {
                    await attempt;

                    return null;
                }
                catch (BotLeaseLostException exception)
                {
                    return exception;
                }
            }));

        outcomes.Count(outcome => outcome is null).ShouldBe(1);
        outcomes[0].ShouldBeNull();
    }

    [Test]
    public async Task DailyLossKillRejectsUnsentSubmitsAndCancelsSentOrdersInTheSameCommit()
    {
        (Bot bot, LeaseHolderId holder) = await LeasedBot();
        var day = new DateOnly(year: 2026, month: 10, day: 25);
        OrderSnapshot entry = BotSeed.MarketBuy(id: 1) with { Status = OrderStatus.Active };
        await Commit(
            new BotRootCommit(
                bot.Id,
                holder,
                Epoch: 1,
                BotSeed.Checkpoint(nextOrderId: 2, entry),
                bot.Parameters,
                [OrderIntent.Submit(OrderIntentId.New(), bot.Id, entry, new ClientOrderId("pg-1"), BotSeed.Now)]),
            stored => stored.RecordDayEquity(day, equity: 100m, _clock.Now));
        await MarkSent(bot);

        await Commit(
            new BotRootCommit(
                bot.Id,
                holder,
                Epoch: 1,
                BotSeed.Checkpoint(nextOrderId: 3, entry, BotSeed.MarketBuy(id: 2)),
                bot.Parameters,
                [OrderIntent.Submit(OrderIntentId.New(), bot.Id, BotSeed.MarketBuy(id: 2), new ClientOrderId("pg-2"), BotSeed.Now)]),
            stored => stored.RecordDayEquity(day, equity: 89m, _clock.Now));

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<string>("SELECT status_reason FROM bots WHERE id = @id", new { id = bot.Id.Value })).ShouldBe("DailyLossLimit");
        (await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM bot_order_intents WHERE bot_id = @id AND engine_order_id = 2",
            new { id = bot.Id.Value })).ShouldBe("Rejected");
        (await connection.ExecuteScalarAsync<bool>(
            "SELECT from_kill FROM bot_order_intents WHERE bot_id = @id AND engine_order_id = 1 AND kind = 'Cancel'",
            new { id = bot.Id.Value })).ShouldBeTrue();
    }

    [Test]
    public async Task DailyLossIsMeasuredFromThePreviousDaysLastStoredEquity()
    {
        (Bot bot, LeaseHolderId holder) = await LeasedBot();
        var day = new DateOnly(year: 2026, month: 10, day: 25);

        await Commit(Root(bot, holder, epoch: 1), stored => stored.RecordDayEquity(day, equity: 100m, _clock.Now));
        await Commit(Root(bot, holder, epoch: 1), stored => stored.RecordDayEquity(day, equity: 95m, _clock.Now));
        await Commit(Root(bot, holder, epoch: 1), stored => stored.RecordDayEquity(day.AddDays(1), equity: 86m, _clock.Now));

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<string?>("SELECT status_reason FROM bots WHERE id = @id", new { id = bot.Id.Value })).ShouldBeNull();

        await Commit(Root(bot, holder, epoch: 1), stored => stored.RecordDayEquity(day.AddDays(1), equity: 85m, _clock.Now));

        (await connection.ExecuteScalarAsync<string?>("SELECT status_reason FROM bots WHERE id = @id", new { id = bot.Id.Value })).ShouldBe("DailyLossLimit");
    }

    private static async Task MarkSent(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("UPDATE bot_order_intents SET status = 'Sent', sent_at = now() WHERE bot_id = @id", new { id = bot.Id.Value });
    }

    private static BotRootCommit Root(Bot bot, LeaseHolderId holder, long epoch)
    {
        return new BotRootCommit(bot.Id, holder, epoch, BotSeed.Checkpoint(), bot.Parameters, []);
    }

    private async Task<(Bot Bot, LeaseHolderId Holder)> LeasedBot()
    {
        Bot bot = await BotSeed.NewBot();
        bot.Start(bot.StrategyVersionId, hasCheckpoint: false, tradingHalted: false, BotSeed.Now);
        var holder = LeaseHolderId.New();
        await InTransaction(async services =>
        {
            await services.GetRequiredService<IBotRepository>().Add(bot, CancellationToken.None);
            await services.GetRequiredService<IBotLeaseRepository>().Add(BotLease.Acquire(bot.Id, holder, BotSeed.Now, LeaseDuration), CancellationToken.None);
        });

        return (bot, holder);
    }

    private async Task Commit(BotRootCommit commit, Action<Bot>? changeBot = null)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BotRootCommitter>().Commit(commit, changeBot, CancellationToken.None);
    }

    private async Task InTransaction(Func<IServiceProvider, Task> action)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await action(scope.ServiceProvider);

                    return true;
                },
                CancellationToken.None);
    }

    private static async Task AssertNothingCommitted(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bot_checkpoints WHERE bot_id = @id", new { id = bot.Id.Value })).ShouldBe(0);
        (await connection.ExecuteScalarAsync<bool>("SELECT has_run FROM bots WHERE id = @id", new { id = bot.Id.Value })).ShouldBeFalse();
    }
}