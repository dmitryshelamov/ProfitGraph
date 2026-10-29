using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     One check of <see cref="TemplateParameterRules" /> over the static values of a compiled graph.
/// </summary>
internal sealed class TemplateParameterCheck(CompiledTradingGraph graph, IReadOnlyDictionary<PortReference, EngineValue> values)
{
    private readonly List<GraphDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, string> _nodes = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<GraphDiagnostic> Run(StrategyTemplateKind template)
    {
        decimal? amount = Positive("buyAmount");

        switch (template)
        {
            case StrategyTemplateKind.SmaCrossover:
                Require(Positive("fastPeriod") < Positive("slowPeriod"), "fastPeriod", "SMA requires fastPeriod < slowPeriod.", ["fastPeriod", "slowPeriod"]);

                break;
            case StrategyTemplateKind.CapitulationDip:
                Require(Positive("drop") < 1m, "drop", "Capitulation dip requires 0 < drop < 1.");
                Require(Positive("volumePeriod") <= 200m, "volumePeriod", "Capitulation dip requires 1 <= volumePeriod <= 200.");
                Positive("volumeMultiple");
                Positive("takeProfit");
                Positive("maxHold");

                break;
            case StrategyTemplateKind.VolumeMomentum:
                Require(Positive("volumePeriod") <= 200m, "volumePeriod", "Volume momentum requires 1 <= volumePeriod <= 200.");
                Require(Positive("stopLoss") < 1m, "stopLoss", "Volume momentum requires 0 < stopLoss < 1.");
                Positive("impulse");
                Positive("volumeMultiple");
                Positive("takeProfit");
                Positive("maxHold");

                break;
            case StrategyTemplateKind.EmaCrossover:
                Require(Positive("emaFast") < Positive("emaSlow"), "emaFast", "EMA crossover requires emaFast < emaSlow.", ["emaFast", "emaSlow"]);
                StopLoss("EMA crossover");

                break;
            case StrategyTemplateKind.MacdTrend:
                Require(Positive("macdFast") < Positive("macdSlow"), "macdFast", "MACD trend requires macdFast < macdSlow.", ["macdFast", "macdSlow"]);
                Positive("macdSignal");
                Positive("trendPeriod");
                StopLoss("MACD trend");

                break;
            case StrategyTemplateKind.RsiReversion:
                decimal? exit = Positive("rsiExit");
                Positive("rsiPeriod");
                Require(Positive("rsiEntry") < exit, "rsiEntry", "RSI reversion requires rsiEntry < rsiExit.", ["rsiEntry", "rsiExit"]);
                Require(exit < 100m, "rsiExit", "RSI reversion requires rsiExit < 100.");
                StopLoss("RSI reversion");

                break;
            case StrategyTemplateKind.BollingerReversion:
                Positive("bandPeriod");
                Positive("bandWidth");
                StopLoss("Bollinger reversion");

                break;
            case StrategyTemplateKind.LevelTrade:
                decimal? entry = Positive("entryPrice");
                Require(Positive("stopPrice") < entry, "stopPrice", "A level trade requires stopPrice < entryPrice.", ["stopPrice", "entryPrice"]);
                Require(entry < Positive("targetPrice"), "targetPrice", "A level trade requires entryPrice < targetPrice.", ["entryPrice", "targetPrice"]);

                break;
            case StrategyTemplateKind.Grid:
                Require(Positive("gridLevels") <= 200m, "gridLevels", "A grid requires 1 <= gridLevels <= 200.");
                Require(Positive("gridStep") < 1m, "gridStep", "A grid requires 0 < gridStep < 1.");
                Require(Positive("gridMaxOpenBuys") <= 200m, "gridMaxOpenBuys", "A grid requires 1 <= gridMaxOpenBuys <= 200.");
                Positive("gridProfitStep");
                Positive("gridLot");
                Positive("gridAnchorCeiling");

                break;
            case StrategyTemplateKind.DcaProfitFox:
                Require(amount <= Positive("allocationCap"), "allocationCap", "DCA requires buyAmount <= allocationCap.", ["buyAmount", "allocationCap"]);
                Positive("interval");

                break;
            default:
                if (template == StrategyTemplateKind.LadderPlus)
                {
                    Require(Positive("stopLoss") < 1m, "stopLoss", "Ladder+ requires 0 < stopLoss < 1.");
                    Positive("profitTarget");
                }

                Require(Positive("drop") < 1m, "drop", "Ladder requires 0 < drop < 1.");
                Require(amount <= Positive("allocationCap"), "allocationCap", "Ladder requires buyAmount <= allocationCap.", ["buyAmount", "allocationCap"]);
                Positive("rise");

                break;
        }

        return _diagnostics.AsReadOnly();
    }

    private void StopLoss(string template)
    {
        Require(Positive("stopLoss") < 1m, "stopLoss", $"{template} requires 0 < stopLoss < 1.");
    }

    /// <summary>
    ///     The static value of a named parameter, null when the strategy has none; a value that is not positive is
    ///     reported and also read as null.
    /// </summary>
    private decimal? Positive(string name)
    {
        CompiledTradingNode[] nodes =
        [
            .. graph.Nodes.Where(node => node.Node.TypeKey == NodeTypeKeys.Parameter &&
                NodeTextSettingResolver.Resolve(node.Node, node.ParameterSpecs, "name") == name)
        ];

        if (nodes.Length != 1 || !values.TryGetValue(new PortReference(nodes[0].Node.Id, "out"), out EngineValue value) || !value.IsAvailable ||
            !UnitAlgebra.IsNumeric(value.ValueType))
        {
            return null;
        }

        if (value.Number > 0m)
        {
            _nodes[name] = nodes[0].Node.Id;

            return value.Number;
        }

        _diagnostics.Add(
            new GraphDiagnostic(GraphDiagnosticCode.InvalidParameter, $"Template parameter '{name}' must be positive.", nodes[0].Node.Id, ParamKey: "value"));

        return null;
    }

    /// <summary>
    ///     Reports a broken relation; a relation with an unknown side (null) holds, unless both named sides are known.
    /// </summary>
    private void Require(bool holds, string parameter, string message, string[]? bothKnown = null)
    {
        bool known = (bothKnown ?? [parameter]).All(_nodes.ContainsKey);

        if (!holds && known)
        {
            _diagnostics.Add(new GraphDiagnostic(GraphDiagnosticCode.InvalidParameter, message, _nodes[parameter], ParamKey: "value"));
        }
    }
}