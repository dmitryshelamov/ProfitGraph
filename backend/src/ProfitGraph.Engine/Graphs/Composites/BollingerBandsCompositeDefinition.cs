using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Engine.Graphs.Composites.CompositeGraphBuilder;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Composites;

/// <summary>
///     Bollinger Bands of the connected series: middle = SMA(period), upper and lower = middle ± multiplier · σ, where σ is
///     the population standard deviation over the same period. All three outputs are ready on the period-th closed candle.
/// </summary>
/// <remarks>The middle band is the SMA node itself, so it equals an SMA block with the same input and period.</remarks>
internal static class BollingerBandsCompositeDefinition
{
    internal static CompositeNodeDefinition Create()
    {
        var graph = new CompositeGraphBuilder();
        graph.Node("middle", NodeTypeKeys.Sma, Params(("period", Number(20m))), schemaVersion: 2);
        graph.Node("deviation", NodeTypeKeys.StandardDeviation, Params(("period", Number(20m))));
        graph.Node("multiplier", NodeTypeKeys.Constant, Params(("value", Number(2m))), ValueType.Scalar, schemaVersion: 2);
        graph.Node("width", NodeTypeKeys.Arithmetic, Params(("op", Text("Multiply"))), schemaVersion: 2);
        graph.Node("upper", NodeTypeKeys.Arithmetic, Params(("op", Text("Add"))), schemaVersion: 2);
        graph.Node("lower", NodeTypeKeys.Arithmetic, Params(("op", Text("Subtract"))), schemaVersion: 2);
        graph.Wire("deviation", "width", "a");
        graph.Wire("multiplier", "width", "b");
        graph.Wire("middle", "upper", "a");
        graph.Wire("width", "upper", "b");
        graph.Wire("middle", "lower", "a");
        graph.Wire("width", "lower", "b");

        return new CompositeNodeDefinition(
            NodeTypeKeys.Bollinger,
            schemaVersion: 1,
            graph.Build(),
            [new PortSpec("in", PortKind.Series, "Series")],
            [
                new PortSpec("upper", PortKind.Series, "Upper band"), new PortSpec("middle", PortKind.Series, "Middle band"),
                new PortSpec("lower", PortKind.Series, "Lower band")
            ],
            [
                new ParamSpec("period", "Period", ParamValueKind.Number, DefaultValue: 20, MinValue: 1, MaxValue: 200, Step: 1),
                new ParamSpec("multiplier", "Standard deviations", ParamValueKind.Number, DefaultValue: 2m, Step: 0.1)
            ],
            [],
            new Dictionary<string, IReadOnlyList<PortReference>>
            {
                ["in"] = [new PortReference("middle", "in"), new PortReference("deviation", "in")]
            },
            new Dictionary<string, PortReference>
            {
                ["upper"] = new PortReference("upper", "out"),
                ["middle"] = new PortReference("middle", "out"),
                ["lower"] = new PortReference("lower", "out")
            },
            new Dictionary<string, IReadOnlyList<PortReference>>
            {
                ["period"] = [new PortReference("middle", "period"), new PortReference("deviation", "period")],
                ["multiplier"] = [new PortReference("multiplier", "value")]
            });
    }
}