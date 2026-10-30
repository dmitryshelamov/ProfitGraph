using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Engine.Graphs.Composites.CompositeGraphBuilder;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Composites;

internal static class PositionFlatCompositeDefinition
{
    internal static CompositeNodeDefinition Create()
    {
        var graph = new CompositeGraphBuilder();
        graph.Node("balance", "Balance");
        graph.Node("position", "Position");
        graph.Node("sellPreview", "OrderSizePreview", Params(("side", Text("Sell")), ("type", Text("Market"))));
        graph.Wire("balance.baseAvailable", "sellPreview", "quantity");
        graph.Node("zeroBase", "Constant", Params(("value", Number(0m))), ValueType.BaseAmount, schemaVersion: 2);
        graph.Compare("zeroPosition", "position.quantity", "zeroBase");

        // The preview's dust flag is known for a sellable balance too, so the check never goes unavailable while holding.
        graph.Binary("flat", "Or", "zeroPosition", "sellPreview.dust");

        return new CompositeNodeDefinition(
            "PositionFlat",
            schemaVersion: 2,
            graph.Build(),
            [],
            [new PortSpec("out", PortKind.Condition, "Условие", ValueType.Boolean)],
            [],
            [],
            new Dictionary<string, IReadOnlyList<PortReference>>(),
            new Dictionary<string, PortReference> { ["out"] = new PortReference("flat", "out") },
            new Dictionary<string, IReadOnlyList<PortReference>>());
    }
}