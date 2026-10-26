using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Application.Bots;

internal static class BotRuns
{
    /// <summary>
    ///     A run that holds and spent nothing has nothing to resume (Profit Fox Designer ADR 0068, widened): no order is
    ///     working or waiting to be submitted or cancelled, no base is held or reserved, no quote is reserved or spent, and no
    ///     fill left a cost, a profit, a fee or a last buy price. Its orders may have ended, rejected or cancelled, without
    ///     any fill; the caller also proves that every intent is final and that the bot has no fills.
    /// </summary>
    internal static bool IsUntraded(GraphEngineCheckpoint checkpoint)
    {
        return checkpoint.Account is
            {
                Orders.IsDefaultOrEmpty: true,
                BaseTotal: 0m,
                BaseReserved: 0m,
                QuoteReserved: 0m,
                CostBasis: 0m,
                RealizedPnl: 0m,
                FeesBase: 0m,
                FeesQuote: 0m,
                LastBuyPrice: null,
                FeeReceipts.IsDefaultOrEmpty: true
            } account && account.QuoteTotal == account.StartingCapital &&
            checkpoint.PendingSubmissionAttempts.IsEmpty && checkpoint.PendingCancellations.IsDefaultOrEmpty;
    }
}