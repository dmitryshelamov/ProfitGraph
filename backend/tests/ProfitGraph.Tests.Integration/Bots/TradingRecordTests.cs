using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotFills;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

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

    /// <summary>
    ///     A venue that charges its commission in quote on both sides reports it late (ADR 0023, TC4): the fill records the
    ///     quote fee asset and the correction is recorded once beside it.
    /// </summary>
    [Test]
    public async Task ALateCommissionIsRecordedOnceAsACorrection_AndAQuoteFeeFillSaysSo()
    {
        var scenario = new RuntimeScenario();
        scenario.Instruments.Rules = symbol => FakeInstrumentSource.Definition(symbol.Value) with
        {
            Rules = new InstrumentRules(
                quantityStep: 0.000001m,
                priceTick: 0.01m,
                minQuantity: 0m,
                minNotional: 5m,
                basePrecision: 6,
                quotePrecision: 8,
                FeeAsset.Quote,
                exactQuoteFees: true)
        };
        using IHost worker = scenario.Worker();
        Bot bot = await RuntimeScenario.RunningLadder(worker);
        await worker.StartAsync();
        await RuntimeScenario.Until(
            async () => (await RuntimeScenario.Checkpoint(worker, bot.Id))?.Engine.Account.Orders is [{ Status: OrderStatus.Active }],
            "the entry is accepted");

        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.Final(orderId: 1, grossBase: 0.1m, grossQuote: 10m, fee: 0m, commissionPending: true));
        await RuntimeScenario.Until(async () => (await Fills(bot)).Count == 1, "the fill is recorded");
        (await Pending(worker, bot)).ShouldBe([true]);
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.CorrectFee(orderId: 1, grossBase: 0.1m, grossQuote: 10m, cumulativeFee: 0.005m));
        scenario.Gateway.Push(bot.Id, ExternalOrderUpdate.CorrectFee(orderId: 1, grossBase: 0.1m, grossQuote: 10m, cumulativeFee: 0.005m));
        await RuntimeScenario.Until(async () => (await Corrections(bot)).Count == 1, "the correction is recorded");
        (await Pending(worker, bot)).ShouldBe([false]);
        await worker.StopAsync();

        (await Corrections(bot)).ShouldBe([(1L, 0m, 0.005m)]);
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        (await connection.QuerySingleAsync<string>("SELECT fee_asset FROM bot_fills WHERE bot_id = @id", new { id = bot.Id.Value })).ShouldBe("Quote");
    }

    /// <summary>
    ///     Whether each fill on the bot's page still waits for its commission.
    /// </summary>
    private static async Task<IReadOnlyList<bool>> Pending(IHost host, Bot bot)
    {
        using IServiceScope scope = host.Services.CreateScope();
        BotFillPageInternal page = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetBotFillsQuery(bot.OwnerId.Value, bot.Id.Value, Before: null, Limit: 10));

        return [.. page.Items.Select(fill => fill.CommissionPending)];
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