using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Application.Bots;

internal static class BotRuns
{
    /// <summary>
    ///     A run that never created an order and holds nothing has nothing to resume (Profit Fox Designer ADR 0068).
    /// </summary>
    internal static bool IsUntraded(GraphEngineCheckpoint checkpoint)
    {
        return checkpoint.Account is { NextOrderId: 0, Orders.IsDefaultOrEmpty: true, BaseTotal: 0m } &&
            checkpoint.PendingSubmissionAttempts.IsEmpty && checkpoint.PendingCancellations.IsDefaultOrEmpty;
    }
}