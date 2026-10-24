using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Engine.Graphs.Composites.CompositeGraphBuilder;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Composites;

internal static class PositionFlatPreviewCompositeDefinition
{
    internal static CompositeNodeDefinition Create()
    {
        var graph = new CompositeGraphBuilder();
        graph.Node("zeroBase", "Constant", Params(("value", Number(0m))), ValueType.BaseAmount, schemaVersion: 2);
        graph.Node("wholePreview", "OrderSizePreview", Params(("side", Text("Sell")), ("type", Text("Market"))));
        graph.Node("flatZero", "Compare", Params(("op", Text("="))), schemaVersion: 2);
        graph.Wire("zeroBase", "flatZero", "b");

        foreach (RejectionReason reason in new[] { RejectionReason.ZeroQuantity, RejectionReason.BelowMinQuantity, RejectionReason.BelowMinNotional })
        {
            graph.Reason("flat" + reason, reason);
            graph.Compare("flatIs" + reason, "wholePreview.reason", "flat" + reason);
        }

        graph.Binary("flatDustFirst", "Or", "flatIsZeroQuantity", "flatIsBelowMinQuantity");
        graph.Binary("flatDust", "Or", "flatDustFirst", "flatIsBelowMinNotional");
        graph.Node("flatInvalid", "Not");
        graph.Wire("wholePreview.valid", "flatInvalid", "in");
        graph.Binary("flatInvalidDust", "And", "flatInvalid", "flatDust");
        graph.Binary("flat", "Or", "flatZero", "flatInvalidDust");

        return new CompositeNodeDefinition(
            "PositionFlatPreview",
            schemaVersion: 1,
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