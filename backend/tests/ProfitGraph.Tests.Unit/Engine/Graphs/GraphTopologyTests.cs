using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Dependencies;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphTopologyTests
{
    [Test]
    public void EmptyGraphReturnsEmptyOrder()
    {
        new GraphTopology().Sort(new StrategyGraph([], [])).ShouldBeEmpty();
    }

    [Test]
    public void IndependentNodesAreOrderedByOrdinalId()
    {
        var graph = new StrategyGraph([GraphNodeGenerator.Create("z"), GraphNodeGenerator.Create("a"), GraphNodeGenerator.Create("A")], []);

        new GraphTopology().Sort(graph).ShouldBe(new[] { "A", "a", "z" });
    }

    [Test]
    public void ConnectionAndWiredBindingToSameNodeCountAsOneDependency()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create(
                    "a",
                    parameters: new Dictionary<string, ParamValue>
                    {
                        ["value"] = new WiredParamValue(new PortReference("z", "out"))
                    }),
                GraphNodeGenerator.Create("z")
            ],
            [GraphNodeGenerator.Connect("z", "a"), GraphNodeGenerator.Connect("z", "a")]);

        new GraphTopology().Sort(graph).ShouldBe(new[] { "z", "a" });
    }

    [Test]
    public void WiredSelfDependencyReturnsCycle()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create(
                    "a",
                    parameters: new Dictionary<string, ParamValue>
                    {
                        ["value"] = new WiredParamValue(new PortReference("a", "out"))
                    })
            ],
            []);

        new GraphTopology().Sort(graph).ShouldBeNull();
    }

    [Test]
    public void UnknownNodesAreLeftForGraphValidation()
    {
        var graph = new StrategyGraph(
            [GraphNodeGenerator.Create("a")],
            [GraphNodeGenerator.Connect("missing", "a"), GraphNodeGenerator.Connect("a", "missing")]);

        new GraphTopology().Sort(graph).ShouldBe(new[] { "a" });
    }
}