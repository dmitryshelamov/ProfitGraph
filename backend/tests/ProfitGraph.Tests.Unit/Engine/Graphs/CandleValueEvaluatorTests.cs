using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class CandleValueEvaluatorTests
{
    [Test]
    public void NumericSmaFixtureCrossesOnlyOnCorrespondingCloseAndPureGuardsProjectMaskedPulse()
    {
        var graph = new StrategyGraph(
            [
                Node("price"), Node("fast", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(2m) }),
                Node("slow", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(3m) }), Node("above", "CrossesAbove"),
                Node("below", "CrossesBelow"), Node("not", "Not"), Node("other", "Not"), Node("and", "And"), Node("nested", "Not")
            ],
            [
                Wire("price", "fast"), Wire("price", "slow"), Wire("fast", "above", "a"), Wire("slow", "above", "b"),
                Wire("fast", "below", "a"), Wire("slow", "below", "b"), Wire("above", "not"), Wire("above", "and", "a"),
                Wire("below", "other"), Wire("other", "and", "b"), Wire("and", "nested")
            ]);
        var evaluator = new CandleValueEvaluator(Compile(graph));
        decimal[] values = [3m, 2m, 1m, 4m, 5m, 1m, 0m];
        var above = new List<int>();
        var below = new List<int>();

        for (var index = 0; index < values.Length; index++)
        {
            Candle candle = Candle(index, values[index]);
            IReadOnlyDictionary<PortReference, EngineValue> advanced = evaluator.AdvanceClosedCandle(candle, index);
            IReadOnlyDictionary<PortReference, EngineValue> close = evaluator.ReadDispatch(Event(candle));

            if (close[new PortReference("above", "out")].Boolean)
            {
                above.Add(index + 1);
                close[new PortReference("and", "out")].Boolean.ShouldBeTrue();
                close[new PortReference("nested", "out")].Boolean.ShouldBeFalse();
            }

            if (close[new PortReference("below", "out")].Boolean)
            {
                below.Add(index + 1);
            }

            for (var dispatch = 0; dispatch < 5; dispatch++)
            {
                IReadOnlyDictionary<PortReference, EngineValue> order = evaluator.ReadDispatch(Event(candle, close: false));
                order[new PortReference("above", "out")].Boolean.ShouldBeFalse();
                order[new PortReference("not", "out")].Boolean.ShouldBeTrue();
                order[new PortReference("and", "out")].Boolean.ShouldBeFalse();
                order[new PortReference("nested", "out")].Boolean.ShouldBeTrue();
                EngineValue cached = order[new PortReference("fast", "out")];
                cached.IsAvailable.ShouldBe(advanced[new PortReference("fast", "out")].IsAvailable);

                if (cached.IsAvailable)
                {
                    cached.Number.ShouldBe(advanced[new PortReference("fast", "out")].Number);
                }
            }
        }

        above.ShouldBe(new[] { 4 });
        below.ShouldBe(new[] { 6 });
    }

    [Test]
    public void WarmupOrdinalDoesNotHideFirstReplayClosePulse()
    {
        var graph = new StrategyGraph(
            [Node("price"), Node("previous", "PreviousCandleValue"), Node("cross", "CrossesAbove")],
            [Wire("price", "previous"), Wire("price", "cross", "a"), Wire("previous", "cross", "b")]);
        var evaluator = new CandleValueEvaluator(Compile(graph));
        evaluator.AdvanceClosedCandle(Candle(index: 0, close: 3m), candleIndex: 0);
        evaluator.AdvanceClosedCandle(Candle(index: 1, close: 2m), candleIndex: 1);
        Candle replay = Candle(index: 2, close: 4m);
        evaluator.AdvanceClosedCandle(replay, candleIndex: 2);

        evaluator.ReadDispatch(Event(replay, replayIndex: 0))[new PortReference("cross", "out")].Boolean.ShouldBeTrue();
        evaluator.ReadDispatch(Event(replay, close: false, replayIndex: 0))[new PortReference("cross", "out")].Boolean.ShouldBeFalse();
    }

    [Test]
    public void IndicatorDoubleBoundaryPreservesUnitsAvailabilityAndSeparateRunState()
    {
        var graph = new StrategyGraph(
            [Node("price"), Node("mean", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(2m) })],
            [Wire("price", "mean")]);
        CompiledTradingGraph compiled = Compile(graph);
        var first = new CandleValueEvaluator(compiled);
        var second = new CandleValueEvaluator(compiled);
        var price = 9007199254740993.123456789m;
        first.AdvanceClosedCandle(Candle(index: 0, price), candleIndex: 0)[new PortReference("mean", "out")].IsAvailable.ShouldBeFalse();
        EngineValue value = first.AdvanceClosedCandle(Candle(index: 1, price), candleIndex: 1)[new PortReference("mean", "out")];
        value.ValueType.ShouldBe(ValueType.Price);
        value.Number.ShouldBe(9007199254740990m);
        second.AdvanceClosedCandle(Candle(index: 0, price), candleIndex: 0)[new PortReference("mean", "out")].IsAvailable.ShouldBeFalse();
        Should.Throw<ArgumentException>(() => first.AdvanceClosedCandle(Candle(index: 1, price), candleIndex: 1));
    }

    [Test]
    public void PreviousAndSimpleAtrAdvanceOncePerCandle()
    {
        var graph = new StrategyGraph(
            [
                Node("price"), Node("previous", "PreviousCandleValue"),
                Node("atr", "ATR", new Dictionary<string, ParamValue> { ["period"] = Number(2m) })
            ],
            [Wire("price", "previous")]);
        var evaluator = new CandleValueEvaluator(Compile(graph));
        evaluator.AdvanceClosedCandle(Candle(index: 0, close: 10m), candleIndex: 0);
        evaluator.AdvanceClosedCandle(Candle(index: 1, close: 13m), candleIndex: 1)[new PortReference("atr", "out")].IsAvailable.ShouldBeFalse();
        Candle current = Candle(index: 2, close: 17m);
        IReadOnlyDictionary<PortReference, EngineValue> snapshot = evaluator.AdvanceClosedCandle(current, candleIndex: 2);
        snapshot[new PortReference("previous", "out")].Number.ShouldBe(13m);
        snapshot[new PortReference("atr", "out")].Number.ShouldBe(3.5m);
        evaluator.ReadDispatch(Event(current, close: false))[new PortReference("previous", "out")].Number.ShouldBe(13m);
    }

    [Test]
    public void DivisionByZeroIdentifiesNodeAndCandle()
    {
        var graph = new StrategyGraph(
            [
                Node("price"), Node("zero", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(0m) }),
                Node("divide", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Divide") })
            ],
            [Wire("price", "divide", "a"), Wire("zero", "divide", "b")]);
        var evaluator = new CandleValueEvaluator(Compile(graph));
        var error = Should.Throw<GraphExecutionException>(() => evaluator.AdvanceClosedCandle(Candle(index: 0, close: 10m), candleIndex: 0));
        error.NodeId.ShouldBe("divide");
        error.BarIndex.ShouldBe(0);
    }

    [Test]
    public void RawEmptyTextMetadataIsNotCoercedToATypedTag()
    {
        CompiledTradingGraph graph = Compile(
            new StrategyGraph(
                [
                    Node(
                        "parameter",
                        "Parameter",
                        new Dictionary<string, ParamValue>
                            { ["name"] = Text(""), ["value"] = new FixedParamValue(new NumberParamLiteral(7d)) })
                ],
                []));
        var evaluator = new CandleValueEvaluator(graph);
        evaluator.AdvanceClosedCandle(Candle(index: 0, close: 1m), candleIndex: 0)[new PortReference("parameter", "out")].Number.ShouldBe(7m);
    }

    [Test]
    public void ExactOptimizationChoicesAreCopiedAndMoneyArithmeticRetainsUnits()
    {
        var quantity = 0.123456789123456789m;
        var price = 9007199254740993.123456789m;
        var graph = new StrategyGraph(
            [
                Node("price"), Node(
                    "quantity",
                    "Constant",
                    new Dictionary<string, ParamValue>
                        { ["value"] = new DecimalOptimizeParamValue(quantity, quantity + 0.000000000000000001m, Step: 0.000000000000000001m) },
                    ValueType.BaseAmount),
                Node("cost", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Multiply") })
            ],
            [Wire("price", "cost", "a"), Wire("quantity", "cost", "b")]);
        CompiledTradingGraph compiled = Compile(graph);
        var mutable = new Dictionary<PortReference, decimal> { [new PortReference("quantity", "value")] = quantity };
        var choices = new EngineExecutionInputs(mutable);
        mutable[new PortReference("quantity", "value")] = quantity + 0.000000000000000001m;
        var evaluator = new CandleValueEvaluator(compiled, choices);
        EngineValue result = evaluator.AdvanceClosedCandle(Candle(index: 0, price), candleIndex: 0)[new PortReference("cost", "out")];
        result.ValueType.ShouldBe(ValueType.QuoteAmount);
        result.Number.ShouldBe(price * quantity);
    }

    [Test]
    public void AvailabilityDistinguishesWarmupAbsenceFromAvailableZero()
    {
        var graph = new StrategyGraph(
            [
                Node("price"), Node("mean", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(2m) }),
                Node("ready", "IsAvailable")
            ],
            [Wire("price", "mean"), Wire("mean", "ready")]);
        var evaluator = new CandleValueEvaluator(Compile(graph));
        evaluator.AdvanceClosedCandle(Candle(index: 0, close: 0m), candleIndex: 0)[new PortReference("ready", "out")].Boolean.ShouldBeFalse();
        IReadOnlyDictionary<PortReference, EngineValue> values = evaluator.AdvanceClosedCandle(Candle(index: 1, close: 0m), candleIndex: 1);
        values[new PortReference("ready", "out")].Boolean.ShouldBeTrue();
        values[new PortReference("mean", "out")].Number.ShouldBe(0m);
    }

    [Test]
    public void DispatchExternalSnapshotIsCopiedAndRejectsWrongUnits()
    {
        INodeDefinition source = Source("Balance", ValueType.QuoteAmount, NodeClock.Dispatch);
        var graph = new StrategyGraph([new GraphNode("balance", "Balance", schemaVersion: 1, new Dictionary<string, ParamValue>())], []);
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Definitions.Append(source).ToArray()).Graph.ShouldNotBeNull();
        var evaluator = new CandleValueEvaluator(compiled);
        var values = new Dictionary<PortReference, EngineValue> { [new PortReference("balance", "out")] = EngineValue.Numeric(ValueType.QuoteAmount, value: 5m) };
        EventOccurrence occurrence = Event(Candle(index: 0, close: 1m), close: false);
        IReadOnlyDictionary<PortReference, EngineValue> snapshot = evaluator.ReadDispatch(occurrence, values);
        values[new PortReference("balance", "out")] = EngineValue.Numeric(ValueType.QuoteAmount, value: 9m);
        snapshot[new PortReference("balance", "out")].Number.ShouldBe(5m);
        values[new PortReference("balance", "out")] = EngineValue.Numeric(ValueType.Price, value: 5m);
        Should.Throw<ArgumentException>(() => evaluator.ReadDispatch(occurrence, values));
    }
}