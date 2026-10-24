using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Composites;

internal static class NoActiveOrdersCompositeDefinition
{
    internal static CompositeNodeDefinition Create()
    {
        var graph = new CompositeGraphBuilder();
        graph.Node("active", "HasActiveOrders");
        graph.Node("noActive", "Not");
        graph.Wire("active", "noActive", "in");

        return new CompositeNodeDefinition(
            "NoActiveOrders",
            schemaVersion: 1,
            graph.Build(),
            [],
            [new PortSpec("out", PortKind.Condition, "Условие", ValueType.Boolean)],
            [],
            [],
            new Dictionary<string, IReadOnlyList<PortReference>>(),
            new Dictionary<string, PortReference> { ["out"] = new PortReference("noActive", "out") },
            new Dictionary<string, IReadOnlyList<PortReference>>());
    }
}