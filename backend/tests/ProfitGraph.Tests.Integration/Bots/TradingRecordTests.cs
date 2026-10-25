using Dapper;

using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The bot's trading record from venue fills (stage 5.12, ADR 0021): each execution is recorded once, even when the
///     venue repeats it or the bot restarts, and a position cycle opens with the entry and closes when the bot is flat.
/// </summary>
[NonParallelizable]
public sealed class TradingRecordTests
{
    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
    }

    [SetUp]
    public Task Isolate()
    {
        return RuntimeScenario.StopEveryBot();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task FillIsRecordedOnceAndItsPositionCycleOpensAndCloses()
    {
        var scenario = new RuntimeScenario();
        Bot bot;
        OrderSnapshot exit;

        using (IHost first = scenario.Worker())
        {
            bot = await RuntimeScenario.RunningLadder(first);
            await first.StartAsync();
            await RuntimeScenario.Until(
                async () => (await RuntimeScenario.Checkpoint(first, bot.Id))?.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
                "the entry is accepted");

            scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
            scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0.0001m));
            await RuntimeScenario.Until(
                async () => (await RuntimeScenario.Checkpoint(first, bot.Id))!.Engine.Account.Orders.Any(order =>
                    order is { Tag: "ladder.exit", Status: OrderStatus.Active }),
                "the exit is accepted");
            exit = (await RuntimeScenario.Checkpoint(first, bot.Id))!.Engine.Account.Orders.Single(order => order.Tag == "ladder.exit");
            await first.StopAsync();
        }

        using IHost second = scenario.Worker();
        await second.StartAsync();
        decimal quantity = exit.Quantity!.Value;
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(exit.Id, quantity, quantity * exit.LimitPrice!.Value, fee: 0.01m));
        await RuntimeScenario.Until(async () => (await Cycles(bot)).Any(cycle => cycle.ClosedAt is not null), "the position cycle closes");
        await second.StopAsync();

        IReadOnlyList<(long EngineOrderId, string Side, decimal GrossBase)> fills = await Fills(bot);
        fills.ShouldBe([(1L, "Buy", 0.1m), (exit.Id, "Sell", quantity)]);
        (DateTime OpenedAt, DateTime? ClosedAt, decimal? RealizedPnl) cycle = (await Cycles(bot)).ShouldHaveSingleItem();
        cycle.RealizedPnl.ShouldNotBeNull().ShouldBeGreaterThan(0m);
    }

    private static async Task<IReadOnlyList<(long EngineOrderId, string Side, decimal GrossBase)>> Fills(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return
        [
            .. await connection.QueryAsync<(long, string, decimal)>(
                "SELECT engine_order_id, side, gross_base FROM bot_fills WHERE bot_id = @id ORDER BY sequence",
                new { id = bot.Id.Value })
        ];
    }

    private static async Task<IReadOnlyList<(DateTime OpenedAt, DateTime? ClosedAt, decimal? RealizedPnl)>> Cycles(Bot bot)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return
        [
            .. await connection.QueryAsync<(DateTime, DateTime?, decimal?)>(
                "SELECT opened_at, closed_at, realized_pnl FROM bot_position_cycles WHERE bot_id = @id",
                new { id = bot.Id.Value })
        ];
    }
}