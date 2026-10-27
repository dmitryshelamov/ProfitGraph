using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Proves, from the broker's own records, that a DAY order T-Invest had accepted and no longer finds was removed by the
///     exchange with nothing executed (ADR 0030). Every check must hold, in this order, and the first that does not stops the
///     proof; a read that fails is a failed check, never a pass:
///     <list type="number">
///         <item>
///             <description>the account has no active order on the share with the attempt's request ID or exchange order ID;</description>
///         </item>
///         <item>
///             <description>neither the engine nor any attempt of the order booked an execution;</description>
///         </item>
///         <item>
///             <description>T-Invest does not know the order by its exchange order ID either;</description>
///         </item>
///         <item>
///             <description>the trading day of the order has ended, and the settle delay has passed;</description>
///         </item>
///         <item>
///             <description>the account's operations on the share since the order was planned show no execution, even a partial one;</description>
///         </item>
///         <item>
///             <description>the account's position in the share equals the position the engine expects, with nothing blocked.</description>
///         </item>
///     </list>
///     The proof reads only; it changes nothing at T-Invest or in the module's tables.
/// </summary>
internal sealed class TInvestAbsenceProofs(
    TInvestOrderReads orders,
    TInvestOperationReads operations,
    ITInvestOrderAttempts attempts,
    TInvestSchedules schedules,
    IOptions<TInvestOptions> options,
    TimeProvider clock,
    ILogger<TInvestAbsenceProofs> logger)
{
    /// <summary>
    ///     A margin for the difference between the clocks of this process and T-Invest around the order's first instant.
    /// </summary>
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The caller has already read the order by its request ID and got no answer, which is the proof's first check.
    /// </summary>
    public async Task<TInvestAbsenceProof> Evaluate(
        TInvestBotContext context,
        TInvestOrderAttempt attempt,
        string exchangeOrderId,
        IReadOnlyList<OrderState> onShare,
        ReconcileOrderInternal order,
        decimal? expectedBase,
        CancellationToken cancellationToken)
    {
        List<string> passed = [TInvestAbsenceChecks.NotFoundByRequestId];

        try
        {
            return await Check(context, attempt, exchangeOrderId, onShare, order, expectedBase, passed, cancellationToken);
        }
        catch (TInvestRpcException exception)
        {
            logger.LogWarning("The proof of a missing T-Invest order could not read the broker: {Failure}", exception.Kind);

            return Fail(passed, TInvestAbsenceChecks.ReadFailed);
        }
    }

    private async Task<TInvestAbsenceProof> Check(
        TInvestBotContext context,
        TInvestOrderAttempt attempt,
        string exchangeOrderId,
        IReadOnlyList<OrderState> onShare,
        ReconcileOrderInternal order,
        decimal? expectedBase,
        List<string> passed,
        CancellationToken cancellationToken)
    {
        if (onShare.Any(active => active.OrderId == exchangeOrderId || active.OrderRequestId == attempt.RequestId.ToString("D")))
        {
            return Fail(passed, TInvestAbsenceChecks.ActiveOrderFound);
        }

        passed.Add(TInvestAbsenceChecks.NotAmongActiveOrders);

        if (order.ExecutedBase > 0m || (await attempts.ForOrder(attempt.BotId, attempt.EngineOrderId, cancellationToken)).Any(other => other.ExecutedLots > 0))
        {
            return Fail(passed, TInvestAbsenceChecks.ExecutionRecorded);
        }

        passed.Add(TInvestAbsenceChecks.NoExecutionRecorded);

        if (await orders.ByExchangeOrderId(context, exchangeOrderId, cancellationToken) is not null)
        {
            return Fail(passed, TInvestAbsenceChecks.KnownByExchangeId);
        }

        passed.Add(TInvestAbsenceChecks.NotFoundByExchangeId);

        DateTimeOffset now = clock.GetUtcNow();

        if (now - attempt.CreatedAt > options.Value.AbsenceMaxWindow)
        {
            return Fail(passed, TInvestAbsenceChecks.OrderTooOld);
        }

        if (await DayEnded(context, attempt, now, cancellationToken) is { } dayFailure)
        {
            return Fail(passed, dayFailure);
        }

        passed.Add(TInvestAbsenceChecks.TradingDayEnded);

        TInvestShareOperations seen = await operations.OfShare(context, attempt.CreatedAt - Skew, now, cancellationToken);

        if (!seen.Complete)
        {
            return Fail(passed, TInvestAbsenceChecks.OperationsIncomplete);
        }

        if (seen.Executed > 0)
        {
            return Fail(passed, TInvestAbsenceChecks.ExecutionInOperations);
        }

        passed.Add(TInvestAbsenceChecks.NoExecutionInOperations);

        if (expectedBase is not { } expected)
        {
            return Fail(passed, TInvestAbsenceChecks.ExpectedPositionUnknown);
        }

        if (await operations.PositionOf(context, cancellationToken) is not { } position)
        {
            return Fail(passed, TInvestAbsenceChecks.PositionUnreadable);
        }

        if (position.Blocked != 0)
        {
            return Fail(passed, TInvestAbsenceChecks.PositionBlocked);
        }

        if (position.Total != expected)
        {
            return Fail(passed, TInvestAbsenceChecks.PositionDiffers);
        }

        passed.Add(TInvestAbsenceChecks.PositionMatchesEngine);

        return new TInvestAbsenceProof(passed, Failed: null);
    }

    /// <summary>
    ///     Null when the trading day that holds the attempt's last recorded instant, which is not earlier than its place
    ///     request, ended at least the settle delay ago; otherwise why not. A day outside the schedule's vocabulary, a gap
    ///     between days and an unreadable schedule are unknown, never ended.
    /// </summary>
    private async Task<string?> DayEnded(TInvestBotContext context, TInvestOrderAttempt attempt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset placed = attempt.UpdatedAt;

        if (await schedules.DaysBetween(context, placed.AddDays(-1), now.AddDays(1), cancellationToken) is not { } days)
        {
            return TInvestAbsenceChecks.TradingDayUnknown;
        }

        if (TInvestSchedules.Holding(days, placed) is not { Known: true, End: { } end })
        {
            return TInvestAbsenceChecks.TradingDayUnknown;
        }

        return now >= end + options.Value.AbsenceSettleDelay ? null : TInvestAbsenceChecks.TradingDayNotEnded;
    }

    private static TInvestAbsenceProof Fail(List<string> passed, string failed)
    {
        return new TInvestAbsenceProof(passed, failed);
    }
}