using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Engine.Graphs.Composites.CompositeGraphBuilder;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Composites;

internal static class HasBaseBalanceCompositeDefinition
{
    internal static CompositeNodeDefinition Create()
    {
        var graph = new CompositeGraphBuilder();
        graph.Node("balance", "Balance");
        graph.Node("zeroBase", "Constant", Params(("value", Number(0m))), ValueType.BaseAmount, schemaVersion: 2);
        graph.Node("positiveBase", "Compare", Params(("op", Text(">"))), schemaVersion: 2);
        graph.Wire("balance.baseAvailable", "positiveBase", "a");
        graph.Wire("zeroBase", "positiveBase", "b");

        return new CompositeNodeDefinition(
            "HasBaseBalance",
            schemaVersion: 1,
            graph.Build(),
            [],
            [new PortSpec("out", PortKind.Condition, "Условие", ValueType.Boolean)],
            [new ParamSpec("minQuantity", "Минимальное количество актива", ParamValueKind.Number, DefaultValue: 0m)],
            [
                new EngineInputSpec(
                    "minQuantity",
                    ValueType.BaseAmount,
                    required: false,
                    supportsInline: true,
                    EngineValue.Numeric(ValueType.BaseAmount, value: 0m))
            ],
            new Dictionary<string, IReadOnlyList<PortReference>>(),
            new Dictionary<string, PortReference> { ["out"] = new PortReference("positiveBase", "out") },
            new Dictionary<string, IReadOnlyList<PortReference>> { ["minQuantity"] = [new PortReference("zeroBase", "value")] });
    }
}