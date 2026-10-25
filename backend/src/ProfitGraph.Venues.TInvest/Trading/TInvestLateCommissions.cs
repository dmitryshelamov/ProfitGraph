using Microsoft.Extensions.Logging;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.MarketData;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Late T-Invest commissions (ADR 0023, T13). A fill applies at once; a final attempt whose executed commission was
///     still zero is read again with <c>GetOrderState</c> until T-Invest reports a positive commission in roubles, for at
///     most <see cref="TInvestOptions.CommissionPendingWindow" />. The commission is recorded on the attempt once and reaches
///     the engine as one fee correction of the order's slice that ends with that attempt. The engine applies it only when
///     the earlier slices' fees are known, and never guesses a split. A decrease, a commission in another currency, or a
///     read that fails changes nothing and is logged by kind only. Only bots whose runner in this process reads updates are
///     considered, so the correction reaches the engine.
/// </summary>
internal sealed class TInvestLateCommissions(
    ITInvestOrderAttempts attempts,
    ITInvestBotContexts contexts,
    TInvestOrderReads reads,
    TInvestOrderStreams streams,
    TInvestOrderUpdates updates,
    TimeProvider clock,
    ILogger<TInvestLateCommissions> logger)
{
    public async Task Round(TimeSpan window, CancellationToken cancellationToken)
    {
        IReadOnlyList<TInvestOrderAttempt> pending;

        try
        {
            pending = await attempts.PendingFees(clock.GetUtcNow() - window, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("The T-Invest pending commissions could not be read: {Failure}", exception.GetType().Name);

            return;
        }

        foreach (IGrouping<Guid, TInvestOrderAttempt> bot in pending.GroupBy(attempt => attempt.BotId))
        {
            var botId = new BotId(bot.Key);

            if (!streams.IsOpen(botId))
            {
                continue;
            }

            try
            {
                TInvestBotContext context = await contexts.Get(botId, cancellationToken);

                foreach (TInvestOrderAttempt attempt in bot)
                {
                    await Check(context, attempt, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("A T-Invest bot's late commissions could not be read: {Failure}", exception.GetType().Name);
            }
        }
    }

    private async Task Check(TInvestBotContext context, TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        if (await reads.ByRequestId(context, attempt.RequestId, cancellationToken) is not { ExecutedCommission: { } reported } ||
            !string.Equals(reported.Currency, "rub", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fee = reported.ToDecimal();

        if (fee <= 0m)
        {
            return;
        }

        TInvestOrderAttempt corrected = attempt with { ExecutedFee = fee, UpdatedAt = clock.GetUtcNow() };

        if (!await attempts.Transition(corrected, TInvestOrderAttemptState.Final, cancellationToken))
        {
            return;
        }

        TInvestExecution through = (await attempts.ForOrder(attempt.BotId, attempt.EngineOrderId, cancellationToken))
            .Where(other => other.State == TInvestOrderAttemptState.Final && other.Attempt <= attempt.Attempt)
            .Select(other => other.Attempt == attempt.Attempt ? corrected : other)
            .Aggregate(TInvestExecution.None, (sum, other) => sum.Add(new TInvestExecution(other.ExecutedLots, other.ExecutedQuote, other.ExecutedFee)));
        updates.Publish(
            context.BotId,
            [
                ExternalOrderUpdate.CorrectFee(
                    attempt.EngineOrderId,
                    through.Lots * context.Share.Lot,
                    through.Quote,
                    through.Fee,
                    commissionPending: false,
                    commissionPartitioned: true)
            ]);
    }
}