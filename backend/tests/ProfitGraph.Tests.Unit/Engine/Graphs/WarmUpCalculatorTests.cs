using System.Globalization;

using NSubstitute;

using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class WarmUpCalculatorTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(50)]
    public void SmaAloneRequiresItsFullPeriodBeforeTheFirstOpen(int period)
    {
        var graph = new StrategyGraph(
            [Node("price"), Node("mean", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(period) })],
            [Wire("price", "mean")]);
        Compile(graph).WarmupCandles.ShouldBe(period);
    }

    [TestCase("SMA", arg2: 3)]
    [TestCase("ATR", arg2: 4)]
    [TestCase("PreviousCandleValue", arg2: 2)]
    [TestCase("CrossesAbove", arg2: 2)]
    [TestCase("CrossesBelow", arg2: 2)]
    public void DeclaredDemandMakesHistoryReadyBeforeFirstReplayCandle(string type, int demand)
    {
        var graph = new StrategyGraph(
            [Node("price"), Node("indicator", type, type is "SMA" or "ATR" ? new Dictionary<string, ParamValue> { ["period"] = Number(3m) } : null)],
            type == "ATR"
                ? []
                : type.StartsWith("Crosses", StringComparison.Ordinal)
                    ? [Wire("price", "indicator", "a"), Wire("price", "indicator", "b")]
                    : [Wire("price", "indicator")]);
        CompiledTradingGraph compiled = Compile(graph);
        compiled.WarmupCandles.ShouldBe(demand);
        var evaluator = new CandleValueEvaluator(compiled);

        for (int index = -demand; index < 0; index++)
        {
            evaluator.AdvanceClosedCandle(Candle(index, 10m + index), index + demand);
        }

        EngineValue value = evaluator.ReadDispatch(Event(Candle(index: 0, close: 20m), close: false))[new PortReference("indicator", "out")];
        value.IsAvailable.ShouldBeTrue();

        if (type.StartsWith("Crosses", StringComparison.Ordinal))
        {
            value.Boolean.ShouldBeFalse();
        }
    }

    [Test]
    public void NestedWindowsPreviousAndCrossComposePrecedingSampleDemands()
    {
        var graph = new StrategyGraph(
            [
                Node("price"), Node("first", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(3m) }),
                Node("second", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(4m) }), Node("previous", "PreviousCandleValue"),
                Node("cross", "CrossesAbove")
            ],
            [Wire("price", "first"), Wire("first", "second"), Wire("second", "previous"), Wire("previous", "cross", "a"), Wire("price", "cross", "b")]);
        CompiledTradingGraph compiled = Compile(graph);
        new WarmUpCalculator().Calculate(compiled).ShouldBe(8);
        var evaluator = new CandleValueEvaluator(compiled);

        for (var index = 0; index < compiled.WarmupCandles; index++)
        {
            evaluator.AdvanceClosedCandle(Candle(index, 10m + index), index);
            evaluator.AreIndicatorsReady.ShouldBe(index == compiled.WarmupCandles - 1);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OptimizeAndWiredPeriodUseMaximumRegardlessOfSelectedValue(bool wired)
    {
        GraphNode period = Node(
            "period",
            "Constant",
            new Dictionary<string, ParamValue> { ["value"] = new DecimalOptimizeParamValue(Min: 2m, Max: 6m, Step: 2m) },
            ValueType.Integer);
        ParamValue setting = wired ? new WiredParamValue(new PortReference("period", "out")) : new DecimalOptimizeParamValue(Min: 2m, Max: 6m, Step: 2m);
        var graph = new StrategyGraph(
            wired
                ? [Node("price"), period, Node("mean", "SMA", new Dictionary<string, ParamValue> { ["period"] = setting })]
                : [Node("price"), Node("mean", "SMA", new Dictionary<string, ParamValue> { ["period"] = setting })],
            [Wire("price", "mean")]);
        CompiledTradingGraph compiled = Compile(graph);
        compiled.WarmupCandles.ShouldBe(6);
        var choices = new EngineExecutionInputs(
            new Dictionary<PortReference, decimal> { [new PortReference(wired ? "period" : "mean", wired ? "value" : "period")] = 2m });
        new EngineParameterResolver().ValidateSelections(compiled, choices).IsValid.ShouldBeTrue();
        new CandleValueEvaluator(compiled, choices).AdvanceClosedCandle(Candle(index: 0, close: 1m), candleIndex: 0)
            [new PortReference("mean", "out")]
            .IsAvailable.ShouldBeFalse();
    }

    [Test]
    public void DynamicPeriodIsDiagnosedBeforeAnyReplay()
    {
        INodeDefinition source = Source("DynamicPeriod", ValueType.Integer, NodeClock.CandleClocked);
        var graph = new StrategyGraph(
            [
                Node("price"), new GraphNode("period", "DynamicPeriod", schemaVersion: 1, new Dictionary<string, ParamValue>()),
                Node("mean", "SMA", new Dictionary<string, ParamValue> { ["period"] = new WiredParamValue(new PortReference("period", "out")) })
            ],
            [Wire("price", "mean")]);
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, Definitions.Append(source).ToArray());
        result.Graph.ShouldBeNull();
        result.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnboundedIndicatorPeriod &&
            diagnostic.NodeId == "mean" && diagnostic.ParamKey == "period");
    }

    [Test]
    public void OverflowIsAStructuredWarmupDiagnostic()
    {
        var sma = Substitute.For<INodeDefinition>();
        sma.TypeKey.Returns("SMA");
        sma.ResolveClock(Arg.Any<NodeClock>()).Returns(NodeClock.CandleClocked);
        sma.IsCandleClocked.Returns(true);
        sma.WarmupPeriodKey.Returns("period");
        sma.WarmupOffset.Returns(-1);
        sma.ResolveOutputType(Arg.Any<GraphNode>(), Arg.Any<Func<string, ValueType?>>(), Arg.Any<List<GraphDiagnostic>>())
            .Returns(call => call.ArgAt<Func<string, ValueType?>>(1)("in"));
        sma.SchemaVersion.Returns(2);
        sma.Inputs.Returns(new[] { new PortSpec("in", PortKind.Series, "Input") });
        sma.Outputs.Returns(new[] { new PortSpec("out", PortKind.Series, "Mean") });
        sma.Params.Returns(new[] { new ParamSpec("period", "Period", ParamValueKind.Number, DefaultValue: 1, MinValue: 1, int.MaxValue, Step: 1) });
        sma.EngineInputs.Returns(Array.Empty<EngineInputSpec>());
        var graph = new StrategyGraph(
            [
                Node("price"), Node("mean", "SMA", new Dictionary<string, ParamValue> { ["period"] = Number(int.MaxValue) }),
                Node("previous", "PreviousCandleValue"), Node("older", "PreviousCandleValue")
            ],
            [Wire("price", "mean"), Wire("mean", "previous"), Wire("previous", "older")]);
        TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(
            graph,
            Definitions.Where(definition => definition.TypeKey != "SMA").Append(sma).ToArray());
        result.Graph.ShouldBeNull();
        result.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.WarmupOverflow && diagnostic.NodeId == "older");
    }

    [Test]
    public void SurplusOptimizationReferenceIsRejectedBeforeHistory()
    {
        CompiledTradingGraph compiled = Compile(new StrategyGraph([Node("price")], []));
        var choices = new EngineExecutionInputs(new Dictionary<PortReference, decimal> { [new PortReference("deleted", "period")] = 2m });
        new EngineParameterResolver().ValidateSelections(compiled, choices)
            .Diagnostics.ShouldContain(diagnostic =>
                diagnostic.Code == GraphDiagnosticCode.UnknownParameter && diagnostic.NodeId == "deleted" && diagnostic.ParamKey == "period");
        Should.Throw<ArgumentException>(() => new CandleValueEvaluator(compiled, choices));
    }

    [TestCase(null)]
    [TestCase("1")]
    [TestCase("3")]
    [TestCase("8")]
    [TestCase("2.5")]
    public void OptimizationChoicesValidateMissingBoundsStepAndIntegerBeforeHistory(string? selected)
    {
        var graph = new StrategyGraph(
            [
                Node("price"), Node(
                    "mean",
                    "SMA",
                    new Dictionary<string, ParamValue>
                        { ["period"] = new DecimalOptimizeParamValue(Min: 2m, Max: 6m, Step: 2m) })
            ],
            [Wire("price", "mean")]);
        CompiledTradingGraph compiled = Compile(graph);
        var choices = new Dictionary<PortReference, decimal>();

        if (selected is not null)
        {
            choices[new PortReference("mean", "period")] = decimal.Parse(selected, CultureInfo.InvariantCulture);
        }

        var inputs = new EngineExecutionInputs(choices);
        GraphValidationResult validation = new EngineParameterResolver().ValidateSelections(compiled, inputs);
        validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.InvalidLiteralRange && diagnostic.ParamKey == "period");
        Should.Throw<ArgumentException>(() => new CandleValueEvaluator(compiled, inputs));
    }
}