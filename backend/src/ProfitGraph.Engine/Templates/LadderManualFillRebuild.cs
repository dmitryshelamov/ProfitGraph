using ProfitGraph.Engine.Blocks;

using static ProfitGraph.Engine.Templates.StrategyBlockExpressions;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     The Ladder's reaction to the owner's order filled through the bot (ADR 0037): the fill cancels the averaging order and the
///     exit, and the template's own wires place the exit again on the whole position, the averaging order by its conditions, and
///     start a new cycle when the position is closed. The averaging order has its own cancel block here because the template's
///     <c>cancelAverage</c> continues to the residual sale. Used by the resume-capable Ladder template and, for strategies saved
///     before it existed, by <see cref="StrategyTemplateUpgrades" /> (ADR 0038).
/// </summary>
internal static class LadderManualFillRebuild
{
    public const string FilledBlockId = "manualFilled";
    public const string CancelAverageBlockId = "manualCancelAverage";

    public static IReadOnlyList<StrategyBlock> Blocks()
    {
        return
        [
            new StrategyBlock(FilledBlockId, "event", "Ручная заявка исполнена", "OnOrderFilled", tag: Tag("manual")),
            new StrategyBlock(CancelAverageBlockId, "cancel", "Отмена усреднения", tag: Tag("ladder.average"))
        ];
    }

    public static IReadOnlyList<StrategyWire> Wires()
    {
        return
        [
            Wire(FilledBlockId, "out", CancelAverageBlockId),
            Wire(CancelAverageBlockId, "completed", "cancelExit"),
            Wire(CancelAverageBlockId, "completed", "restartEvent")
        ];
    }
}
