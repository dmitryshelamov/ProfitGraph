using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Applies what T-Invest reported for an attempt (ADR 0023): translates it with the executions of the order's earlier
///     attempts, records the exchange order ID, the state and, once final, the attempt's own execution, and continues a
///     carry-over: a cancelled carried attempt with whole lots left becomes the next attempt, waiting with
///     <see cref="OrderWaitingCode.CarryOver" /> for a later trading date. Only the remainder is placed again, never the full
///     quantity.
/// </summary>
internal sealed class TInvestOrderProgress(ITInvestOrderAttempts attempts, ITInvestOrderWaits waits, TimeProvider clock)
{
    public async Task<IReadOnlyList<ExternalOrderUpdate>> Apply(
        TInvestBotContext context,
        TInvestOrderAttempt attempt,
        OrderState state,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TInvestOrderAttempt> all = await attempts.ForOrder(attempt.BotId, attempt.EngineOrderId, cancellationToken);
        TInvestExecution earlier = all.Where(other => other.Attempt < attempt.Attempt && other.State == TInvestOrderAttemptState.Final)
            .Aggregate(TInvestExecution.None, (sum, other) => sum.Add(new TInvestExecution(other.ExecutedLots, other.ExecutedQuote, other.ExecutedFee)));
        IReadOnlyList<ExternalOrderUpdate> updates = TInvestOrderTranslation.Updates(attempt, state, context.Share.Lot, earlier);
        await Record(attempt, state, context.Share.Lot, cancellationToken);

        long remainder = state.LotsRequested - state.LotsExecuted;

        if (attempt.CancelledBy == TInvestCancelReason.CarryOver &&
            state.ExecutionReportStatus == OrderExecutionReportStatus.ExecutionReportStatusCancelled &&
            remainder > 0 &&
            all.All(other => other.Attempt <= attempt.Attempt))
        {
            await Continue(context, attempt, remainder, cancellationToken);
        }

        return updates;
    }

    private async Task Record(TInvestOrderAttempt attempt, OrderState state, decimal lot, CancellationToken cancellationToken)
    {
        bool final = TInvestOrderTranslation.IsFinal(state);
        TInvestOrderAttemptState next = final ? TInvestOrderAttemptState.Final : TInvestOrderAttemptState.Active;
        string? exchangeOrderId = string.IsNullOrEmpty(state.OrderId) ? attempt.ExchangeOrderId : state.OrderId;

        if (attempt.State == TInvestOrderAttemptState.Final || (attempt.State == next && attempt.ExchangeOrderId == exchangeOrderId))
        {
            return;
        }

        TInvestExecution own = final ? TInvestOrderTranslation.Executed(state, lot) : TInvestExecution.None;
        TInvestOrderAttempt recorded = attempt with
        {
            State = next,
            ExchangeOrderId = exchangeOrderId,
            ExecutedLots = own.Lots,
            ExecutedQuote = own.Quote,
            ExecutedFee = own.Fee,
            UpdatedAt = clock.GetUtcNow()
        };
        await attempts.Transition(recorded, attempt.State, cancellationToken);
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