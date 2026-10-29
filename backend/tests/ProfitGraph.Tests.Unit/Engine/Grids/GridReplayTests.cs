using System.Text.Json;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Engine.Grids;

/// <summary>
///     The grid block in replay (ADR 0058): levels below the anchor, a sale per bought level, the order limit, the upward shift,
///     the stop below the grid and determinism.
/// </summary>
public sealed class GridReplayTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void ALevelBuysBelowTheAnchorSellsAtItsProfitStepAndBuysAgain()
    {
        BacktestResult result = Run(GridStrategyBlockFactory.Create(levels: 3), LimitFillMode.Touch, Bar(index: 0, open: 100m, low: 98.5m, high: 100m, close: 99m));

        result.Terminal.Outcome.ShouldBe(
            BacktestOutcome.Completed,
            string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        Fills(result).ShouldBe(["buy 99 grid.grid.1.buy", "sell 99.99 grid.grid.1.sell", "buy 99 grid.grid.1.buy"]);
    }

    [Test]
    public void ThroughFillsLeaveABuyAtTheCloseResting()
    {
        BacktestResult result = Run(GridStrategyBlockFactory.Create(levels: 3), LimitFillMode.Through, Bar(index: 0, open: 100m, low: 98.5m, high: 100m, close: 99m));

        Fills(result).ShouldBe(["buy 99 grid.grid.1.buy", "sell 99.99 grid.grid.1.sell"]);
    }

    [Test]
    public void EachLevelSellsWhatItBoughtNetOfTheFee()
    {
        BacktestResult result = Run(
            GridStrategyBlockFactory.Create(levels: 2, profitStep: 0.05m),
            LimitFillMode.Touch,
            Bar(index: 0, open: 100m, low: 97m, high: 100m, close: 98m),
            fee: 0.001m);

        BacktestLedgerEntry[] buys = result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled && entry.Fill!.Side == OrderSide.Buy).ToArray();
        buys.Length.ShouldBe(2);
        BacktestLedgerEntry[] sells = result.Ledger.Where(entry => entry is { Kind: BacktestLedgerKind.Submitted, Order.Side: OrderSide.Sell }).ToArray();
        sells.Select(entry => entry.Order!.Quantity!.Value)
            .ToArray()
            .ShouldBe(buys.Select(entry => decimal.Floor(entry.Fill!.BaseChange * 100000000m) / 100000000m).ToArray());
        sells.Select(entry => entry.Order!.LimitPrice).ShouldAllBe(price => price > 0m);
        result.Terminal.Account.BaseReserved.ShouldBe(0m);
    }

    [Test]
    public void AtMostMaxOpenBuysRest()
    {
        BacktestResult result = Run(
            GridStrategyBlockFactory.Create(levels: 5, maxOpenBuys: 2),
            LimitFillMode.Touch,
            Bar(index: 0, open: 100m, low: 100m, high: 100m, close: 100m));

        result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Submitted)
            .Select(entry => entry.Order!.Tag)
            .ShouldBe(["grid.grid.1.buy", "grid.grid.2.buy"]);
    }

    [Test]
    public void AnEmptyGridShiftsUpAtACloseAboveItsThreshold()
    {
        BacktestResult result = Run(
            GridStrategyBlockFactory.Create(levels: 1, shiftThreshold: 0.02m, anchorCeiling: 0.05m),
            LimitFillMode.Touch,
            Bar(index: 0, open: 100m, low: 100m, high: 103m, close: 103m),
            Bar(index: 1, open: 110m, low: 110m, high: 110m, close: 110m));

        result.Ledger.Where(entry => entry.Kind is BacktestLedgerKind.Submitted or BacktestLedgerKind.Cancelled)
            .Select(entry => FormattableString.Invariant($"{entry.Kind} {entry.Order!.LimitPrice:0.########}"))
            .ShouldBe(["Submitted 99", "Cancelled 99", "Submitted 101.97"]);
    }

    [Test]
    public void AFallBelowTheStopSellsTheLevelsAtMarketAndStopsTheGrid()
    {
        BacktestResult result = Run(
            GridStrategyBlockFactory.Create(levels: 2, stopBelow: 0.05m),
            LimitFillMode.Touch,
            Bar(index: 0, open: 100m, low: 90m, high: 100m, close: 90m),
            Bar(index: 1, open: 91m, low: 80m, high: 91m, close: 85m));

        // The first candle runs Open → Low → High → Close: both levels buy, sell on the way up, buy again on the way down.
        Fills(result)
            .ShouldBe(
            [
                "buy 99 grid.grid.1.buy", "buy 98.01 grid.grid.2.buy", "sell 99 grid.grid.2.sell", "sell 99.99 grid.grid.1.sell", "buy 99 grid.grid.1.buy",
                "buy 98.01 grid.grid.2.buy", "sell 91 grid.grid.stop"
            ]);
        result.Terminal.Account.BaseTotal.ShouldBeLessThan(0.0001m);
        result.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Context.CandleIndex == 1).ShouldBe(0);
    }

    [Test]
    public void IdenticalInputsGiveIdenticalLedgers()
    {
        Candle[] candles = Enumerable.Range(start: 0, count: 64)
            .Select(index => Bar(index, open: 100m, 95m + index % 4, 103m + index % 3, 98m + index % 5))
            .ToArray();
        StrategyDefinition definition = GridStrategyBlockFactory.Create(levels: 6, step: 0.005m, profitStep: 0.008m, maxOpenBuys: 3);

        BacktestResult first = Run(definition, LimitFillMode.Through, candles);
        BacktestResult second = Run(definition, LimitFillMode.Through, candles);

        first.FillCount.ShouldBeGreaterThan(10);
        JsonSerializer.Serialize(second.Ledger).ShouldBe(JsonSerializer.Serialize(first.Ledger));
    }

    [Test]
    public void ACancelBlockNamingTheGridStopsItWithoutSelling()
    {
        StrategyDefinition template = GridStrategyBlockFactory.Create(levels: 2, profitStep: 0.05m);
        var definition = new StrategyDefinition(
            template.Parameters,
            template.Values,
            [
                .. template.Blocks, new StrategyBlock("close", "event", "Закрытие свечи", "OnCandleClosed"),
                new StrategyBlock("stopGrid", "cancel", "Остановить сетку", grid: "grid")
            ],
            [.. template.Wires, new StrategyWire(new StrategyExit("close", "out"), "stopGrid")],
            template.TemplateKind);

        BacktestResult result = Run(
            definition,
            LimitFillMode.Touch,
            Bar(index: 0, open: 100m, low: 98.5m, high: 100m, close: 99m),
            Bar(index: 1, open: 99m, low: 90m, high: 99m, close: 95m));

        Fills(result).ShouldBe(["buy 99 grid.grid.1.buy"]);
        result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Cancelled)
            .Select(entry => entry.Order!.Tag)
            .ShouldBe(["grid.grid.1.sell", "grid.grid.2.buy"]);
        result.Terminal.Account.BaseTotal.ShouldBeGreaterThan(1m);
        result.Ledger.ShouldNotContain(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Context.CandleIndex == 1);
    }

    [Test]
    public void ASettingThatReadsTheMarketIsRejected()
    {
        StrategyDefinition template = GridStrategyBlockFactory.Create();
        StrategyBlock grid = template.Blocks.Single(block => block.Kind == "grid");
        Dictionary<string, ValueExpression> settings = grid.Settings.ToDictionary(pair => pair.Key, pair => pair.Value);
        settings["lot"] = new ValueExpression("balance", field: "quoteAvailable");
        var changed = new StrategyDefinition(
            template.Parameters,
            template.Values,
            template.Blocks.Select(block => block.Kind == "grid" ? new StrategyBlock(block.Id, "grid", block.DisplayName, settings: settings) : block),
            template.Wires,
            template.TemplateKind);

        new StrategyDefinitionValidator().Validate(changed, ProductionTradingNodeTests.Catalog)
            .Diagnostics
            .ShouldContain(diagnostic => diagnostic.Message.Contains("lot", StringComparison.Ordinal));
    }

    private static string[] Fills(BacktestResult result)
    {
        return result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled)
            .Select(entry => FormattableString.Invariant($"{entry.Fill!.Side.ToString().ToLowerInvariant()} {entry.Fill.Price:0.########} {entry.Fill.Tag}"))
            .ToArray();
    }

    private static BacktestResult Run(StrategyDefinition definition, LimitFillMode mode, params Candle[] candles)
    {
        return Run(definition, mode, candles, fee: 0m);
    }

    private static BacktestResult Run(StrategyDefinition definition, LimitFillMode mode, Candle candle, decimal fee)
    {
        return Run(definition, mode, [candle], fee);
    }

    private static BacktestResult Run(StrategyDefinition definition, LimitFillMode mode, IEnumerable<Candle> candles, decimal fee)
    {
        var input = new BacktestInput(
            Compile(definition),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, fee, fee).WithLimitFill(mode),
            new InstrumentRules(quantityStep: 0.00000001m, priceTick: 0.01m, minQuantity: 0m, minNotional: 1m, basePrecision: 8, quotePrecision: 8),
            Timeframe.Parse("1h"),
            [],
            candles);

        return new BacktestEngine().Run(input);
    }

    private static CompiledTradingGraph Compile(StrategyDefinition definition)
    {
        new StrategyDefinitionValidator().Validate(definition, ProductionTradingNodeTests.Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, ProductionTradingNodeTests.Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }

    private static Candle Bar(int index, decimal open, decimal low, decimal high, decimal close)
    {
        return new Candle(Start.AddHours(index), open, high, low, close, Volume: 10m);
    }
}