using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Engine.Graphs.Composites.CompositeGraphBuilder;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Composites;

internal static class PositionFlatPreviewCompositeDefinition
{
    internal static CompositeNodeDefinition Create()
    {
        // Flat at a zero quantity or when selling all of it is rejected as too small. The preview's dust flag is known for a sellable
        // quantity too, so the check never goes unavailable while the position can be sold.
        var graph = new CompositeGraphBuilder();
        graph.Node("zeroBase", "Constant", Params(("value", Number(0m))), ValueType.BaseAmount, schemaVersion: 2);
        graph.Node("wholePreview", "OrderSizePreview", Params(("side", Text("Sell")), ("type", Text("Market"))));
        graph.Node("flatZero", "Compare", Params(("op", Text("="))), schemaVersion: 2);
        graph.Wire("zeroBase", "flatZero", "b");
        graph.Binary("flat", "Or", "flatZero", "wholePreview.dust");

        return new CompositeNodeDefinition(
            "PositionFlatPreview",
            schemaVersion: 2,
            graph.Build(),
            [new PortSpec("quantity", PortKind.Series, "Количество актива", ValueType.BaseAmount)],
            [
                new PortSpec("out", PortKind.Condition, "Условие", ValueType.Boolean),
                new PortSpec("valid", PortKind.Condition, "Продажа возможна", ValueType.Boolean)
            ],
            [],
            [],
            new Dictionary<string, IReadOnlyList<PortReference>>
            {
                ["quantity"] = [new PortReference("wholePreview", "quantity"), new PortReference("flatZero", "a")]
            },
            new Dictionary<string, PortReference>
            {
                ["out"] = new PortReference("flat", "out"),
                ["valid"] = new PortReference("wholePreview", "valid")
            },
            new Dictionary<string, IReadOnlyList<PortReference>>());
    }
}