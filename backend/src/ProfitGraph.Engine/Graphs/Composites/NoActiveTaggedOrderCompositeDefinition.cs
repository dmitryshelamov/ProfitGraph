using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Engine.Graphs.Composites.CompositeGraphBuilder;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Composites;

internal static class NoActiveTaggedOrderCompositeDefinition
{
    internal static CompositeNodeDefinition Create()
    {
        var graph = new CompositeGraphBuilder();
        graph.Node("activeentry", "ActiveOrder", Params(("tag", Text("ladder.entry"))));
        graph.Node("noentry", "Not");
        graph.Wire("activeentry.exists", "noentry", "in");

        return new CompositeNodeDefinition(
            "NoActiveTaggedOrder",
            schemaVersion: 1,
            graph.Build(),
            [],
            [
                new PortSpec("out", PortKind.Condition, "Условие", ValueType.Boolean),
                new PortSpec("exists", PortKind.Condition, "Ордер существует", ValueType.Boolean)
            ],
            [new ParamSpec("tag", "Метка ордера", ParamValueKind.Text, "ladder.entry")],
            [],
            new Dictionary<string, IReadOnlyList<PortReference>>(),
            new Dictionary<string, PortReference>
            {
                ["out"] = new PortReference("noentry", "out"),
                ["exists"] = new PortReference("activeentry", "exists")
            },
            new Dictionary<string, IReadOnlyList<PortReference>> { ["tag"] = [new PortReference("activeentry", "tag")] });
    }
}