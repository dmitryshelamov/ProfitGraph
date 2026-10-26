using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.MarketData;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Turns T-Invest's state of an attempt (<c>OrdersService/GetOrderState</c>) into cumulative engine updates of its engine
///     order in the order the engine accepts (ADR 0023). An attempt's executed base is its executed lots times the lot; its
///     executed quote is the sum of its trade stages, each a price per share times its lots times the lot, so no average is
///     trusted; its fee is the executed commission in roubles. Earlier attempts of the same engine order, closed by a
///     carry-over or a band refusal, are added, so the engine always hears the order's totals. A fill whose commission is
///     still zero is reported as pending. T-Invest's commission is per order and cumulative, so a fee that grows after a
///     known one belongs to the new executions: fills are partitioned, and the engine itself keeps a fee pending while an
///     earlier one is. A cancellation ends the engine order only when the owner or the strategy asked for
///     it; one for a carry-over keeps it working; one nobody asked for is <see cref="BotStatusReason.UnresolvedOrder" />.
///     Anything that cannot be accounted exactly is unresolved.
/// </summary>
internal static class TInvestOrderTranslation
{
    public static IReadOnlyList<ExternalOrderUpdate> Updates(TInvestOrderAttempt attempt, OrderState state, decimal lot, TInvestExecution earlier)
    {
        long engineOrderId = attempt.EngineOrderId;

        if (state.ExecutionReportStatus == OrderExecutionReportStatus.ExecutionReportStatusRejected)
        {
            return earlier.Lots == 0
                ? [ExternalOrderUpdate.Reject(engineOrderId, RejectionReason.BrokerRejected, "T-Invest rejected the order.")]
                : throw new TInvestOrderUnresolvedException("RejectedAfterFills");
        }

        TInvestExecution own = Executed(state, lot);
        TInvestExecution total = earlier.Add(own);
        decimal grossBase = total.Lots * lot;
        bool pending = own.Lots > 0 && own.Fee == 0m;
        List<ExternalOrderUpdate> updates = [ExternalOrderUpdate.Accept(engineOrderId)];

        switch (state.ExecutionReportStatus)
        {
            case OrderExecutionReportStatus.ExecutionReportStatusFill:
                updates.Add(ExternalOrderUpdate.Final(engineOrderId, grossBase, total.Quote, total.Fee, pending, commissionPartitioned: true));

                break;
            case OrderExecutionReportStatus.ExecutionReportStatusCancelled:
                if (attempt.CancelledBy is null)
                {
                    throw new OrderGatewaySafetyException(BotStatusReason.UnresolvedOrder, "T-Invest cancelled an order nobody asked to cancel.");
                }

                if (grossBase > 0m)
                {
                    updates.Add(ExternalOrderUpdate.Partial(engineOrderId, grossBase, total.Quote, total.Fee, pending, commissionPartitioned: true));
                }

                if (attempt.CancelledBy == TInvestCancelReason.Owner)
                {
                    updates.Add(ExternalOrderUpdate.ConfirmCancel(engineOrderId));
                }

                break;
            case OrderExecutionReportStatus.ExecutionReportStatusNew or OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill:
                if (grossBase > 0m)
                {
                    updates.Add(ExternalOrderUpdate.Partial(engineOrderId, grossBase, total.Quote, total.Fee, pending, commissionPartitioned: true));
                }

                break;
            default:
                throw new TInvestOrderUnresolvedException("ExecutionStatusUnexpected");
        }

        return updates;
    }

    /// <summary>
    ///     The attempt's own execution, checked: stages must add up to the executed lots, in roubles.
    /// </summary>
    public static TInvestExecution Executed(OrderState state, decimal lot)
    {
        long stageLots = state.Stages.Sum(stage => stage.Quantity);

        if (stageLots != state.LotsExecuted)
        {
            // The executed lots and the trade stages are two parts of one answer that can be read apart while the order is filling.
            throw new TInvestOrderUnresolvedException("StagesDisagreeWithExecutedLots", isTransient: true);
        }

        if (state.Stages.Any(stage => stage.Price is null))
        {
            throw new TInvestOrderUnresolvedException("StagePriceMissing");
        }

        if (state.Stages.Any(stage => !Roubles(stage.Price)))
        {
            throw new TInvestOrderUnresolvedException("StagePriceNotInRoubles");
        }

        if (state.ExecutedCommission is { } reported && !Roubles(reported) && !IsZero(reported))
        {
            throw new TInvestOrderUnresolvedException("CommissionNotInRoubles");
        }

        return new TInvestExecution(
            state.LotsExecuted,
            state.Stages.Sum(stage => stage.Price.ToDecimal() * stage.Quantity * lot),
            state.ExecutedCommission?.ToDecimal() ?? 0m);
    }

    /// <summary>
    ///     Whether the state can no longer change.
    /// </summary>
    public static bool IsFinal(OrderState state)
    {
        return state.ExecutionReportStatus is OrderExecutionReportStatus.ExecutionReportStatusFill or OrderExecutionReportStatus.ExecutionReportStatusRejected
            or OrderExecutionReportStatus.ExecutionReportStatusCancelled;
    }

    /// <summary>
    ///     A zero amount carries no currency meaning: T-Invest may leave the currency empty on a commission that is not charged yet.
    /// </summary>
    private static bool IsZero(MoneyValue money)
    {
        return money.Units == 0 && money.Nano == 0;
    }

    private static bool Roubles(MoneyValue money)
    {
        return string.Equals(money.Currency, "rub", StringComparison.OrdinalIgnoreCase);
    }
}