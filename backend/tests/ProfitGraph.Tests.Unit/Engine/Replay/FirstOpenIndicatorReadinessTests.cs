using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;
using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class FirstOpenIndicatorReadinessTests
{
    [Test]
    public void OnStartBuyFilledDispatchReadsSmaOnCandleZero()
    {
        var graph = new StrategyGraph(
            [
                Node("start", "OnStart"), Buy("buy"), Node("close"), Node("sma", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(3m) }),
                Limit("proof", price: null)
            ],
            [Wire("start", "buy", "trigger"), Wire("close", "sma"), Edge("buy", "filled", "proof", "trigger"), Wire("sma", "proof", "price")]);
        BacktestResult result = Replay(graph);
        result.Ledger.Single(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Order!.OriginNodeId == "proof")
            .Context.CandleIndex.ShouldBe(0);
    }

    [Test]
    public void FiftyPeriodVolatilityExitWithoutGuardKeepsSmaLadderFillsWhenScalingIsZero()
    {
        StrategyDefinition template = LadderStrategyBlockFactory.Create(drop: 0.07m, rise: 0.075m);
        var period = new ValueExpression("constant", ValueType.Integer, number: 50m);

        ValueExpression Sma(string field) => new ValueExpression(
            "SMA",
            arguments: new Dictionary<string, ValueExpression>
            {
                ["in"] = new ValueExpression("candle", field: field), ["period"] = period
            });

        ValueExpression volatility = Arithmetic("Divide", Arithmetic("Subtract", Sma("High"), Sma("Low")), new ValueExpression("candle", field: "Close"));
        ValueExpression effectiveRise = Arithmetic(
            "Max",
            new ValueExpression("parameter", name: "rise"),
            Arithmetic("Multiply", new ValueExpression("constant", ValueType.Scalar, number: 0m), volatility));
        ValueExpression exit = Arithmetic(
            "Multiply",
            new ValueExpression("position", field: "averageCost"),
            Arithmetic("Add", new ValueExpression("constant", ValueType.Scalar, number: 1m), effectiveRise));
        var next = new StrategyValue(
            "nextPrice",
            Arithmetic(
                "Multiply",
                Sma("Close"),
                Arithmetic("Subtract", new ValueExpression("constant", ValueType.Scalar, number: 1m), new ValueExpression("parameter", name: "drop"))));
        var baseline = new StrategyDefinition(
            template.Parameters,
            template.Values.Select(value => value.Name == "nextPrice" ? next : value),
            template.Blocks,
            template.Wires);
        var scaled = new StrategyDefinition(
            baseline.Parameters,
            baseline.Values.Select(value => value.Name == "exitPrice" ? new StrategyValue("exitPrice", exit) : value),
            baseline.Blocks,
            baseline.Wires);
        BacktestResult expected = Replay(new StrategyDefinitionLowerer(Catalog).Lower(baseline).Graph);
        BacktestResult actual = Replay(new StrategyDefinitionLowerer(Catalog).Lower(scaled).Graph);
        expected.FillCount.ShouldBeGreaterThan(1);
        actual.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled)
            .Select(entry => entry.Fill)
            .ShouldBe(expected.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled).Select(entry => entry.Fill));
    }

    private static ValueExpression Arithmetic(string operation, ValueExpression a, ValueExpression b)
    {
        return new ValueExpression("arithmetic", operation: operation, arguments: new Dictionary<string, ValueExpression> { ["a"] = a, ["b"] = b });
    }

    private static BacktestResult Replay(StrategyGraph graph)
    {
        TradingGraphCompilationResult compiled = new TradingGraphCompiler().Compile(graph, Catalog);
        compiled.Validation.Diagnostics.ShouldBeEmpty();
        CompiledTradingGraph plan = compiled.Graph.ShouldNotBeNull();
        var input = new BacktestInput(
            plan,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 3000m, slippage: 0.0005m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6),
            Timeframe.Parse("1h"),
            Enumerable.Range(-plan.WarmupCandles, plan.WarmupCandles).Select(index => Bar(index, open: 100m, low: 85m, high: 110m, close: 105m)),
            Enumerable.Range(start: 0, count: 20).Select(index => Bar(index, open: 100m, low: 85m, high: 110m, close: 105m)));

        return new BacktestEngine().Run(input);
    }
}