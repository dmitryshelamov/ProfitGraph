using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     Ladder with the saved SMA averaging rules and recovery after a live session resumes.
/// </summary>
public static class LadderSmaStrategyBlockFactory
{
    public static StrategyDefinition Create(decimal buyAmount = 100m, decimal allocationCap = 300m, decimal drop = 0.1m, decimal rise = 0.1m, int smaPeriod = 50)
    {
        StrategyDefinition original = LadderResumeStrategyBlockFactory.Create(buyAmount, allocationCap, drop, rise);
        List<StrategyBlock> blocks = original.Blocks.ToList();
        blocks.Insert(index: 1, new StrategyBlock("clock", "event", "Закрытие свечи", "OnCandleClosed"));
        int averageIndex = blocks.FindIndex(block => block.Id == "averageConditions");
        StrategyBlock averageConditions = blocks[averageIndex];
        List<StrategyCheck> checks = averageConditions.Checks.ToList();
        checks.Insert(index: 2, NoTag("ladder.exit", exists: true));
        checks.Insert(checks.Count - 1, Compare("<", Value("nextPrice"), Position("lastBuyPrice")));
        blocks[averageIndex] = new StrategyBlock(
            averageConditions.Id,
            averageConditions.Kind,
            averageConditions.DisplayName,
            join: averageConditions.Join,
            checks: checks);
        List<StrategyWire> wires = original.Wires.ToList();
        int resumeWireIndex = wires.FindIndex(wire => wire.From.Block == "resume");
        wires.Insert(resumeWireIndex, Wire("clock", "out", "averageConditions"));

        return new StrategyDefinition(
            [.. original.Parameters, new StrategyParameter("smaPeriod", ValueType.Integer, smaPeriod, StrategyParameterDisplayUnit.None)],
            [
                new StrategyValue("close", new ValueExpression("candle", field: "Close")),
                new StrategyValue(
                    "sma",
                    new ValueExpression("SMA", arguments: new Dictionary<string, ValueExpression> { ["in"] = Value("close"), ["period"] = Parameter("smaPeriod") })),
                new StrategyValue(
                    "nextPrice",
                    Arithmetic("Multiply", Value("sma"), Arithmetic("Subtract", Constant(number: 1m, ValueType.Scalar), Parameter("drop")))),
                .. original.Values.Where(value => value.Name != "nextPrice")
            ],
            blocks,
            wires,
            StrategyTemplateKind.LadderSma);
    }
}