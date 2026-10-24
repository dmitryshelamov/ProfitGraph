namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Classifies status reasons. A safety reason never clears itself: the runtime does not restart the bot, and only
///     the owner can start it again. A control reason marks a bot that a kill switch or the daily loss limit stopped.
/// </summary>
public static class BotStatusReasons
{
    public static bool IsSafety(BotStatusReason reason)
    {
        return reason is not (BotStatusReason.RuntimeFailure or BotStatusReason.Killed or BotStatusReason.DailyLossLimit);
    }

    public static bool IsControl(BotStatusReason reason)
    {
        return reason is BotStatusReason.Killed or BotStatusReason.DailyLossLimit;
    }

    /// <summary>
    ///     Reasons that a Start on a bot that should already run clears, so the owner can retry after fixing the cause
    ///     (Profit Fox Designer ADR 0054 and 0055 revisions).
    /// </summary>
    public static bool IsRestartableByStart(BotStatusReason reason)
    {
        return reason is BotStatusReason.IntentFailed or BotStatusReason.InsufficientBalance;
    }
}