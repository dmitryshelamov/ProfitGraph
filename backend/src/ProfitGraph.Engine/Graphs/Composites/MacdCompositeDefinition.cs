using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Engine.Graphs.Composites.CompositeGraphBuilder;

namespace ProfitGraph.Engine.Graphs.Composites;

/// <summary>
///     MACD of the connected series: macd = EMA(fastPeriod) − EMA(slowPeriod), signal = EMA(signalPeriod) of macd and
///     histogram = macd − signal, all from the decimal EMA node.
/// </summary>
/// <remarks>
///     The macd line is ready on the max(fastPeriod, slowPeriod)-th closed candle; the signal EMA seeds on the following
///     signalPeriod − 1 lines, so signal and histogram, and the node's warm-up, need slowPeriod + signalPeriod − 1 candles.
/// </remarks>
internal static class MacdCompositeDefinition
{
    internal static CompositeNodeDefinition Create()
    {
        var graph = new CompositeGraphBuilder();
        graph.Node("fast", NodeTypeKeys.Ema, Params(("period", Number(12m))));
        graph.Node("slow", NodeTypeKeys.Ema, Params(("period", Number(26m))));
        graph.Node("line", NodeTypeKeys.Arithmetic, Params(("op", Text("Subtract"))), schemaVersion: 2);
        graph.Node("signal", NodeTypeKeys.Ema, Params(("period", Number(9m))));
        graph.Node("histogram", NodeTypeKeys.Arithmetic, Params(("op", Text("Subtract"))), schemaVersion: 2);
        graph.Wire("fast", "line", "a");
        graph.Wire("slow", "line", "b");
        graph.Wire("line", "signal", "in");
        graph.Wire("line", "histogram", "a");
        graph.Wire("signal", "histogram", "b");

        return new CompositeNodeDefinition(
            NodeTypeKeys.Macd,
            schemaVersion: 1,
            graph.Build(),
            [new PortSpec("in", PortKind.Series, "Series")],
            [
                new PortSpec("macd", PortKind.Series, "MACD line"), new PortSpec("signal", PortKind.Series, "Signal line"),
                new PortSpec("histogram", PortKind.Series, "Histogram")
            ],
            [
                new ParamSpec("fastPeriod", "Fast period", ParamValueKind.Number, DefaultValue: 12, MinValue: 1, MaxValue: 200, Step: 1),
                new ParamSpec("slowPeriod", "Slow period", ParamValueKind.Number, DefaultValue: 26, MinValue: 1, MaxValue: 200, Step: 1),
                new ParamSpec("signalPeriod", "Signal period", ParamValueKind.Number, DefaultValue: 9, MinValue: 1, MaxValue: 200, Step: 1)
            ],
            [],
            new Dictionary<string, IReadOnlyList<PortReference>>
            {
                ["in"] = [new PortReference("fast", "in"), new PortReference("slow", "in")]
            },
            new Dictionary<string, PortReference>
            {
                ["macd"] = new PortReference("line", "out"),
                ["signal"] = new PortReference("signal", "out"),
                ["histogram"] = new PortReference("histogram", "out")
            },
            new Dictionary<string, IReadOnlyList<PortReference>>
            {
                ["fastPeriod"] = [new PortReference("fast", "period")],
                ["slowPeriod"] = [new PortReference("slow", "period")],
                ["signalPeriod"] = [new PortReference("signal", "period")]
            });
    }
}