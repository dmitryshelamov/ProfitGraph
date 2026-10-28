using System.Globalization;

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
///             <description>
///                 the trading day of the order has ended, and the settle delay has passed (by the schedule, or for a MOEX share
///                 by the Moscow calendar when the schedule cannot be read, ADR 0032);
///             </description>
///         </item>
///         <item>
///             <description>
///                 the account's operations on the share since the order was planned show no execution, even a partial one, except
///                 operations whose every trade number belongs to another executed order of the same bot (ADR 0033);
///             </description>
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

        (string? dayFailure, bool byCalendar) = await DayEnded(context, attempt, now, cancellationToken);

        if (dayFailure is not null)
        {
            return Fail(passed, dayFailure);
        }

        passed.Add(TInvestAbsenceChecks.TradingDayEnded);

        if (byCalendar)
        {
            passed.Add(TInvestAbsenceChecks.TradingDayEndedByCalendar);
        }

        DateTimeOffset windowStart = attempt.CreatedAt - options.Value.AbsenceClockSkew;
        IReadOnlyDictionary<string, long> ownTrades = await OwnTrades(context, attempt, windowStart, cancellationToken);
        TInvestShareOperations seen = await operations.OfShare(context, windowStart, now, ownTrades, cancellationToken);

        if (!seen.Complete)
        {
            return Fail(passed, TInvestAbsenceChecks.OperationsIncomplete);
        }

        if (seen.Executed > 0)
        {
            return Fail(passed, TInvestAbsenceChecks.ExecutionInOperations);
        }

        passed.Add(TInvestAbsenceChecks.NoExecutionInOperations);

        if (seen.OwnOrders.Count > 0)
        {
            passed.Add(OwnExecutionsExcluded(seen.OwnOrders));
        }

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
    ///     The journal code of the operations excluded as the bot's own: <c>OwnExecutionsExcluded:{count}:{engine orders joined by +}</c>.
    /// </summary>
    private static string OwnExecutionsExcluded(IReadOnlyList<long> orders)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{TInvestAbsenceChecks.OwnExecutionsExcluded}:{orders.Count}:{string.Join(separator: '+', orders.Distinct().Order())}");
    }

    /// <summary>
    ///     The trade numbers of the bot's other orders on the share that executed in the window (ADR 0033), each tied to its
    ///     engine order: the stages T-Invest keeps for the attempt's exchange order ID. An attempt without an exchange order
    ///     ID, one T-Invest does not answer for, one whose stages do not add up to the lots the bot booked, and a stage without
    ///     a trade number ties nothing. The checked order's own attempts never count.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, long>> OwnTrades(
        TInvestBotContext context,
        TInvestOrderAttempt attempt,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var trades = new Dictionary<string, long>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);

        foreach (TInvestOrderAttempt other in await attempts.ExecutedOnShare(attempt.BotId, attempt.InstrumentUid, since, cancellationToken))
        {
            if (other.EngineOrderId == attempt.EngineOrderId || other.ExchangeOrderId is not { } exchangeOrderId)
            {
                continue;
            }

            if (await orders.ByExchangeOrderId(context, exchangeOrderId, cancellationToken) is not { } state ||
                state.Stages.Sum(stage => stage.Quantity) != other.ExecutedLots)
            {
                continue;
            }

            foreach (string tradeId in state.Stages.Select(stage => stage.TradeId).Where(id => !string.IsNullOrEmpty(id)))
            {
                if (!trades.TryAdd(tradeId, other.EngineOrderId) && trades[tradeId] != other.EngineOrderId)
                {
                    ambiguous.Add(tradeId);
                }
            }
        }

        foreach (string tradeId in ambiguous)
        {
            trades.Remove(tradeId);
        }

        return trades;
    }

    /// <summary>
    ///     Null failure when the trading day that holds the attempt's last recorded instant, which is not earlier than its place
    ///     request, ended at least the settle delay ago; otherwise why not. A day outside the schedule's vocabulary and a gap
    ///     between days are unknown, never ended. T-Invest answers the schedule for today and the next week only (ADR 0030), so
    ///     when it cannot be read the calendar of Moscow time ends the day of a MOEX share (ADR 0032); any other exchange stays
    ///     unknown. <c>ByCalendar</c> tells the proof which of the two ended the day.
    /// </summary>
    private async Task<(string? Failure, bool ByCalendar)> DayEnded(
        TInvestBotContext context,
        TInvestOrderAttempt attempt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateTimeOffset placed = attempt.UpdatedAt > attempt.CreatedAt ? attempt.UpdatedAt : attempt.CreatedAt;

        if (await schedules.DaysBetween(context, placed.AddDays(-1), now.AddDays(1), cancellationToken) is not { } days)
        {
            return (CalendarDayEnded(context, placed, now), ByCalendar: true);
        }

        if (TInvestSchedules.Holding(days, placed) is not { Known: true, End: { } end })
        {
            return (TInvestAbsenceChecks.TradingDayUnknown, ByCalendar: false);
        }

        return (now >= end + options.Value.AbsenceSettleDelay ? null : TInvestAbsenceChecks.TradingDayNotEnded, ByCalendar: false);
    }

    private string? CalendarDayEnded(TInvestBotContext context, DateTimeOffset placed, DateTimeOffset now)
    {
        if (!TInvestMoscowCalendar.Covers(context.Share.Exchange) || placed > now + options.Value.AbsenceFutureSkew)
        {
            return TInvestAbsenceChecks.TradingDayUnknown;
        }

        if (now < TInvestMoscowCalendar.DayEnd(placed) + options.Value.AbsenceSettleDelay)
        {
            return TInvestAbsenceChecks.TradingDayNotEnded;
        }

        logger.LogInformation("The trading schedule is unavailable; the trading day of a missing MOEX order is ended by the Moscow calendar.");

        return null;
    }

    private static TInvestAbsenceProof Fail(List<string> passed, string failed)
    {
        return new TInvestAbsenceProof(passed, failed);
    }
}