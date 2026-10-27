using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     Selectable Ladder revision that restores missing orders when a live session resumes and rebuilds its grid when the owner
///     buys by hand: the manual purchase cancels the averaging order and the exit, and the exit is placed again on the whole
///     position at its new average cost, the averaging order again by the strategy's own rules (ADR 0037).
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
                new StrategyBlock("manualBuy", "event", "Ручная покупка исполнена", "OnOrderFilled", side: "Buy", tag: Tag("manual")),
                new StrategyBlock("manualCancelAverage", "cancel", "Отмена усреднения", tag: Tag("ladder.average")),
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
                Wire("resumeExitGuard", "yes", "exitConditions"),
                Wire("manualBuy", "out", "manualCancelAverage"),
                Wire("manualCancelAverage", "completed", "cancelExit")
            ],
            StrategyTemplateKind.LadderV2);
    }
}