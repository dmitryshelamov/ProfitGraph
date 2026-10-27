using ProfitGraph.Engine.Blocks;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     What the Ladder does when the bot resumes (a start, a reconciliation that changed its orders, an order the venue cancelled, ADR
///     0035 and ADR 0039): it starts a cycle when the position is flat, and places the exit again when the position has base balance
///     and no exit order. Used by the resume-capable Ladder template and, for strategies saved before it existed, by
///     <see cref="StrategyTemplateUpgrades" /> (ADR 0040).
/// </summary>
internal static class LadderResumeRestore
{
    public const string ResumeBlockId = "resume";
    public const string ExitGuardBlockId = "resumeExitGuard";

    public static IReadOnlyList<StrategyBlock> Blocks()
    {
        return
        [
            new StrategyBlock(ResumeBlockId, "event", "Возобновление", "OnResume"),
            new StrategyBlock(
                ExitGuardBlockId,
                "condition",
                "Восстановить выход?",
                join: "all",
                checks: [new StrategyCheck("HasBaseBalance"), NoTag("ladder.exit")])
        ];
    }

    public static IReadOnlyList<StrategyWire> Wires()
    {
        return
        [
            Wire(ResumeBlockId, "out", "entryConditions"),
            Wire(ResumeBlockId, "out", ExitGuardBlockId),
            Wire(ExitGuardBlockId, "yes", "exitConditions")
        ];
    }
}
