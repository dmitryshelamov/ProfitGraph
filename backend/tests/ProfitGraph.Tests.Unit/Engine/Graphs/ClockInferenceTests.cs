using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class ClockInferenceTests
{
    [Test]
    public void FixedValuesAreStaticAndWiredValuesInheritCandleClock()
    {
        var graph = new StrategyGraph(
            [
                Node("price"), Node("scale", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(2m) }),
                Node("arithmetic", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Multiply") }),
                Node(
                    "parameter",
                    "Parameter",
                    new Dictionary<string, ParamValue> { ["value"] = new WiredParamValue(new PortReference("arithmetic", "out")) },
                    ValueType.Price)
            ],
            [Wire("price", "arithmetic", "a"), Wire("scale", "arithmetic", "b")]);
        CompiledTradingGraph compiled = Compile(graph);
        compiled.Nodes.Single(node => node.Node.Id == "scale").Clock.ShouldBe(NodeClock.Static);
        compiled.Nodes.Single(node => node.Node.Id == "arithmetic").Clock.ShouldBe(NodeClock.CandleClocked);
        compiled.Nodes.Single(node => node.Node.Id == "parameter").Clock.ShouldBe(NodeClock.CandleClocked);
    }

    [TestCase("SMA", NodeClock.Static)]
    [TestCase("SMA", NodeClock.Dispatch)]
    [TestCase("SMA", NodeClock.Event)]
    [TestCase("SMA", NodeClock.Action)]
    [TestCase("PreviousCandleValue", NodeClock.Static)]
    [TestCase("PreviousCandleValue", NodeClock.Dispatch)]
    [TestCase("CrossesAbove", NodeClock.Static)]
    [TestCase("CrossesBelow", NodeClock.Dispatch)]
    public void StatefulSamplesRejectEveryNonCandleClock(string indicator, NodeClock clock)
    {
        INodeDefinition source = Source("TypedSource", ValueType.Price, clock);
        var sourceNode = new GraphNode("source", "TypedSource", schemaVersion: 1, new Dictionary<string, ParamValue>());
        GraphConnection[] connections = indicator.StartsWith("Crosses", StringComparison.Ordinal)
            ? [Wire("source", "indicator", "a"), Wire("source", "indicator", "b")]
            : [Wire("source", "indicator")];
        var graph = new StrategyGraph([sourceNode, Node("indicator", indicator)], connections);
        INodeDefinition[] definitions = Definitions.Append(source).ToArray();

        new GraphValidator().ValidateStrict(graph, definitions)
            .Diagnostics.ShouldContain(diagnostic =>
                diagnostic.Code == GraphDiagnosticCode.SmaInputNotCandleClocked && diagnostic.NodeId == "indicator");
        new TradingGraphCompiler().Compile(graph, definitions).Graph.ShouldBeNull();
    }

    [Test]
    public void PureStateCombinationRemainsDispatchClockedAndCannotFeedIndicator()
    {
        INodeDefinition source = Source("Balance", ValueType.Price, NodeClock.Dispatch);
        var graph = new StrategyGraph(
            [
                new GraphNode("state", "Balance", schemaVersion: 1, new Dictionary<string, ParamValue>()),
                Node("price"), Node("sum", "Arithmetic"), Node("mean", "SMA")
            ],
            [Wire("price", "sum", "a"), Wire("state", "sum", "b"), Wire("sum", "mean")]);
        INodeDefinition[] definitions = Definitions.Append(source).ToArray();
        new GraphValidator().ValidateStrict(graph, definitions)
            .Diagnostics.ShouldContain(diagnostic =>
                diagnostic.Code == GraphDiagnosticCode.SmaInputNotCandleClocked && diagnostic.NodeId == "mean");
    }
}