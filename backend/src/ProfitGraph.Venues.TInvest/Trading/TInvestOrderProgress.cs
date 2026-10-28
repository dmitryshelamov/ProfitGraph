using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Engine.Accounting;

using Tinkoff.InvestApi.V1;

using OrderType = ProfitGraph.Engine.Accounting.OrderType;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Applies what T-Invest reported for an attempt (ADR 0023): records the exchange order ID, the state and, once final, the
///     attempt's own execution, then translates it with the executions of the order's earlier attempts, so a final state is
///     kept even when the translation stops the bot. It continues a carry-over: a cancelled carried attempt with whole lots
///     left becomes the next attempt, waiting with <see cref="OrderWaitingCode.CarryOver" /> for a later trading date. Only the
///     remainder is placed again, never the full quantity. A DAY limit attempt nobody asked to cancel that T-Invest reports
///     cancelled once the trading day that holds it has ended, by the schedule, is recorded as <see cref="TInvestCancelReason.Expired" />
///     and ends the engine order with what it executed, but only while the account's position covers its bots
///     (<see cref="TInvestPositionCoverage" />, ADR 0031); a schedule that cannot be read leaves the state unrecorded and the
///     read is tried again. Any other cancellation nobody asked for is recorded as the owner's own cancel at the broker (ADR 0039).
/// </summary>
internal sealed class TInvestOrderProgress(
    ITInvestOrderAttempts attempts,
    ITInvestOrderWaits waits,
    TInvestSchedules schedules,
    TInvestPositionCoverage coverage,
    TimeProvider clock)
{
    /// <summary>
    ///     Records the state and returns the engine updates. <paramref name="bookedBase" /> is what the engine has already
    ///     booked as executed for the order, when the caller knows it; without it the coverage of an expiry assumes the
    ///     least favourable booking.
    /// </summary>
    public async Task<IReadOnlyList<ExternalOrderUpdate>> Apply(
        TInvestBotContext context,
        TInvestOrderAttempt attempt,
        OrderState state,
        CancellationToken cancellationToken,
        decimal? bookedBase = null)
    {
        TInvestOrderAttempt recorded = await Record(context, attempt, state, cancellationToken);
        TInvestExecution earlier = await Executed(attempt.BotId, attempt.EngineOrderId, attempt.Attempt, cancellationToken);
        IReadOnlyList<ExternalOrderUpdate> updates = TInvestOrderTranslation.Updates(recorded, state, context.Share.Lot, earlier);

        if (recorded.CancelledBy == TInvestCancelReason.Expired && state.ExecutionReportStatus == OrderExecutionReportStatus.ExecutionReportStatusCancelled)
        {
            await Cover(context, recorded, earlier.Add(TInvestOrderTranslation.Executed(state, context.Share.Lot)), bookedBase, cancellationToken);
        }

        return updates;
    }

    /// <summary>
    ///     Records the state without translating it, and continues a carry-over: the post-session sweep keeps the outcome of an
    ///     order no runner reads (ADR 0031). Returns the attempt as stored.
    /// </summary>
    public async Task<TInvestOrderAttempt> Record(TInvestBotContext context, TInvestOrderAttempt attempt, OrderState state, CancellationToken cancellationToken)
    {
        bool final = TInvestOrderTranslation.IsFinal(state);
        TInvestOrderAttemptState next = final ? TInvestOrderAttemptState.Final : TInvestOrderAttemptState.Active;
        string? exchangeOrderId = string.IsNullOrEmpty(state.OrderId) ? attempt.ExchangeOrderId : state.OrderId;
        TInvestOrderAttempt recorded = attempt;

        if (attempt.State != TInvestOrderAttemptState.Final && (attempt.State != next || attempt.ExchangeOrderId != exchangeOrderId))
        {
            TInvestExecution own = final ? TInvestOrderTranslation.Executed(state, context.Share.Lot) : TInvestExecution.None;
            TInvestCancelReason? cancelledBy = attempt.CancelledBy;

            if (cancelledBy is null && state.ExecutionReportStatus == OrderExecutionReportStatus.ExecutionReportStatusCancelled)
            {
                // A cancellation nobody asked for is the end of the trading day when the schedule shows it, and otherwise the owner's own
                // cancel at the broker (ADR 0039): the executions the state shows are booked and the order ends as cancelled.
                cancelledBy = await Expired(context, attempt, cancellationToken) ? TInvestCancelReason.Expired : TInvestCancelReason.Owner;
            }
            TInvestOrderAttempt moved = attempt with
            {
                State = next,
                ExchangeOrderId = exchangeOrderId,
                CancelledBy = cancelledBy,
                ExecutedLots = own.Lots,
                ExecutedQuote = own.Quote,
                ExecutedFee = own.Fee,
                UpdatedAt = clock.GetUtcNow()
            };
            recorded = await attempts.Transition(moved, attempt.State, cancellationToken)
                ? moved
                : await attempts.ByRequestId(attempt.RequestId, cancellationToken) ?? throw new TInvestOrderUnresolvedException("AttemptChanged");
        }

        long remainder = state.LotsRequested - state.LotsExecuted;

        if (recorded.CancelledBy == TInvestCancelReason.CarryOver &&
            state.ExecutionReportStatus == OrderExecutionReportStatus.ExecutionReportStatusCancelled &&
            remainder > 0 &&
            (await attempts.ForOrder(attempt.BotId, attempt.EngineOrderId, cancellationToken)).All(other => other.Attempt <= attempt.Attempt))
        {
            await Continue(context, recorded, remainder, cancellationToken);
        }

        return recorded;
    }

    /// <summary>
    ///     The engine updates of a final attempt T-Invest no longer answers for, from what the module recorded: an owner's
    ///     cancel or an expiry ends the order with the executions of every attempt up to this one. An expiry closes only while
    ///     the account's position covers its bots (ADR 0031).
    /// </summary>
    public async Task<IReadOnlyList<ExternalOrderUpdate>> Recorded(
        TInvestBotContext context,
        TInvestOrderAttempt attempt,
        decimal bookedBase,
        CancellationToken cancellationToken)
    {
        if (attempt is not { State: TInvestOrderAttemptState.Final, CancelledBy: TInvestCancelReason.Owner or TInvestCancelReason.Expired })
        {
            throw new TInvestOrderUnresolvedException("NotRecordedAsCancelled");
        }

        TInvestExecution total = await Executed(attempt.BotId, attempt.EngineOrderId, attempt.Attempt + 1, cancellationToken);

        if (attempt.CancelledBy == TInvestCancelReason.Expired)
        {
            await Cover(context, attempt, total, bookedBase, cancellationToken);
        }

        decimal grossBase = total.Lots * context.Share.Lot;
        List<ExternalOrderUpdate> updates = [ExternalOrderUpdate.Accept(attempt.EngineOrderId)];

        if (grossBase > 0m)
        {
            bool pending = attempt.ExecutedLots > 0 && attempt.ExecutedFee == 0m;
            updates.Add(ExternalOrderUpdate.Partial(attempt.EngineOrderId, grossBase, total.Quote, total.Fee, pending, commissionPartitioned: true));
        }

        updates.Add(ExternalOrderUpdate.ConfirmCancel(attempt.EngineOrderId));

        return updates;
    }

    /// <summary>
    ///     Whether a cancellation of the attempt that nobody asked for would now be the exchange's end of its DAY order: the
    ///     attempt is a limit order and the trading day that holds its last recorded instant, which is not earlier than its
    ///     place request, has ended. A day outside the schedule's vocabulary, or no day, is not ended; a schedule that
    ///     cannot be read leaves the Moscow calendar to end the day of a MOEX share and is otherwise not ended.
    /// </summary>
    public async Task<bool> Expired(TInvestBotContext context, TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.Type != OrderType.Limit || string.IsNullOrEmpty(context.Share.Exchange))
        {
            return false;
        }

        DateTimeOffset now = clock.GetUtcNow();

        // T-Invest answers the schedule for today and the next week only, so a day that has passed is usually refused; the Moscow
        // calendar then ends the day of a MOEX share (ADR 0032), and any other exchange is not expired (ADR 0039): the cancellation is
        // then the owner's, and the strategy restores the order.
        if (await schedules.DaysBetween(context, attempt.UpdatedAt.AddDays(-1), now.AddDays(1), cancellationToken) is not { } days)
        {
            return TInvestMoscowCalendar.Covers(context.Share.Exchange) && now >= TInvestMoscowCalendar.DayEnd(attempt.UpdatedAt);
        }

        return TInvestSchedules.Holding(days, attempt.UpdatedAt) is { Known: true, End: { } end } && now >= end;
    }

    /// <summary>
    ///     Null when the account's position covers its bots once the order's executions the engine has not booked are added;
    ///     otherwise the failed check. Without <paramref name="bookedBase" /> a buy counts as not booked and a sell as booked,
    ///     the stricter of the two.
    /// </summary>
    public Task<string?> Covers(
        TInvestBotContext context,
        TInvestOrderAttempt attempt,
        TInvestExecution total,
        decimal? bookedBase,
        CancellationToken cancellationToken)
    {
        decimal grossBase = total.Lots * context.Share.Lot;
        decimal unbooked = bookedBase is { } booked
            ? attempt.Side == OrderSide.Buy ? grossBase - booked : booked - grossBase
            : attempt.Side == OrderSide.Buy
                ? grossBase
                : 0m;

        return coverage.Check(context, unbooked, cancellationToken);
    }

    /// <summary>
    ///     What the order's attempts numbered below <paramref name="before" /> executed, by what the module recorded.
    /// </summary>
    public async Task<TInvestExecution> Executed(Guid botId, long engineOrderId, int before, CancellationToken cancellationToken)
    {
        return (await attempts.ForOrder(botId, engineOrderId, cancellationToken))
            .Where(other => other.Attempt < before && other.State == TInvestOrderAttemptState.Final)
            .Aggregate(TInvestExecution.None, (sum, other) => sum.Add(new TInvestExecution(other.ExecutedLots, other.ExecutedQuote, other.ExecutedFee)));
    }

    private async Task Cover(TInvestBotContext context, TInvestOrderAttempt attempt, TInvestExecution total, decimal? bookedBase, CancellationToken cancellationToken)
    {
        if (await Covers(context, attempt, total, bookedBase, cancellationToken) is { } failed)
        {
            throw new OrderGatewaySafetyException(
                BotStatusReason.UnresolvedOrder,
                "The account's position does not cover its bots, so an order the exchange ended is left to the owner.",
                $"AutoCheck:{failed}");
        }
    }

    private async Task Continue(TInvestBotContext context, TInvestOrderAttempt carried, long remainder, CancellationToken cancellationToken)
    {
        int number = carried.Attempt + 1;
        DateTimeOffset now = clock.GetUtcNow();
        TInvestOrderAttempt next = carried with
        {
            Attempt = number,
            RequestId = TInvestOrderRequestIds.For(context.BotId, carried.EngineOrderId, number),
            Lots = remainder,
            State = TInvestOrderAttemptState.Planned,
            ExchangeOrderId = null,
            BrokerCode = null,
            WaitingCode = OrderWaitingCode.CarryOver,
            BandLow = null,
            BandHigh = null,
            CancelledBy = null,
            ExecutedLots = 0,
            ExecutedQuote = 0m,
            ExecutedFee = 0m,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (await attempts.Add(next, cancellationToken))
        {
            await waits.Report(context.BotId, carried.EngineOrderId, OrderWaitingCode.CarryOver, low: null, high: null, cancellationToken);
        }
    }
}