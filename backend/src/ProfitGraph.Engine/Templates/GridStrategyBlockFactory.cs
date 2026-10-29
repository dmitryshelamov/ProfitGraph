using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     The «Сетка» template (ADR 0058): the run's start starts a spot long-only grid. Every setting is a parameter, so the
///     number of levels, the steps and the lot can be optimized; the shift and the stop are off until their parameters are set.
/// </summary>
public static class GridStrategyBlockFactory
{
    public static StrategyDefinition Create(
        int levels = 10,
        decimal step = 0.01m,
        decimal profitStep = 0.01m,
        decimal lot = 100m,
        int maxOpenBuys = 10,
        decimal shiftThreshold = 0m,
        decimal anchorCeiling = 0.5m,
        decimal stopBelow = 0m)
    {
        return new StrategyDefinition(
            [
                new StrategyParameter("gridLevels", ValueType.Integer, levels, StrategyParameterDisplayUnit.None),
                new StrategyParameter("gridStep", ValueType.Scalar, step, StrategyParameterDisplayUnit.Percent),
                new StrategyParameter("gridProfitStep", ValueType.Scalar, profitStep, StrategyParameterDisplayUnit.Percent),
                new StrategyParameter("gridLot", ValueType.QuoteAmount, lot, StrategyParameterDisplayUnit.Quote),
                new StrategyParameter("gridMaxOpenBuys", ValueType.Integer, maxOpenBuys, StrategyParameterDisplayUnit.None),
                new StrategyParameter("gridShiftThreshold", ValueType.Scalar, shiftThreshold, StrategyParameterDisplayUnit.Percent),
                new StrategyParameter("gridAnchorCeiling", ValueType.Scalar, anchorCeiling, StrategyParameterDisplayUnit.Percent),
                new StrategyParameter("gridStopBelow", ValueType.Scalar, stopBelow, StrategyParameterDisplayUnit.Percent)
            ],
            [],
            [
                new StrategyBlock("start", "event", "Начало прогона", "OnStart"),
                new StrategyBlock(
                    "grid",
                    "grid",
                    "Сетка",
                    settings: new Dictionary<string, ValueExpression>
                    {
                        ["levels"] = Parameter("gridLevels"),
                        ["step"] = Parameter("gridStep"),
                        ["profitStep"] = Parameter("gridProfitStep"),
                        ["lot"] = Parameter("gridLot"),
                        ["maxOpenBuys"] = Parameter("gridMaxOpenBuys"),
                        ["shiftThreshold"] = Parameter("gridShiftThreshold"),
                        ["anchorCeiling"] = Parameter("gridAnchorCeiling"),
                        ["stopBelow"] = Parameter("gridStopBelow")
                    })
            ],
            [Wire("start", "out", "grid")],
            StrategyTemplateKind.Grid);
    }
}