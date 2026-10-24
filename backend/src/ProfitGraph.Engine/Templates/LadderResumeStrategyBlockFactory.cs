using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     Selectable Ladder revision that restores missing orders when a live session resumes.
/// </summary>
public static class LadderResumeStrategyBlockFactory
{
    public static StrategyDefinition Create(decimal buyAmount = 100m, decimal allocationCap = 300m, decimal drop = 0.1m, decimal rise = 0.1m)
    {
        StrategyDefinition original = LadderStrategyBlockFactory.Create(buyAmount, allocationCap, drop, rise);

        return new StrategyDefinition(
            original.Parameters,
            original.Values,
            [
                .. original.Blocks,
                new StrategyBlock("resume", "event", "Возобновление", "OnResume"),
                new StrategyBlock(
                    "resumeExitGuard",
                    "condition",
                    "Восстановить выход?",
                    join: "all",
                    checks: [new StrategyCheck("HasBaseBalance"), NoTag("ladder.exit")])
            ],
            [
                .. original.Wires,
                Wire("resume", "out", "entryConditions"),
                Wire("resume", "out", "resumeExitGuard"),
                Wire("resumeExitGuard", "yes", "exitConditions")
            ],
            StrategyTemplateKind.LadderV2);
    }
}