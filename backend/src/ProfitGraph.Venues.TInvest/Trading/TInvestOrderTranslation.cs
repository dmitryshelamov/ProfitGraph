using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.MarketData;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Turns T-Invest's order state (<c>OrdersService/GetOrderState</c>) into cumulative engine updates in the order the
///     engine accepts (ADR 0023). Executed base is the executed lots times the lot; executed quote is the sum of the trade
///     stages, each a price per share times its lots times the lot, so no average is trusted. The fee is the executed
///     commission in roubles; a fill whose commission is still zero is reported as pending. Anything that cannot be
///     accounted exactly, such as stages that disagree with the executed lots or another currency, is unresolved.
/// </summary>
internal static class TInvestOrderTranslation
{
    public static IReadOnlyList<ExternalOrderUpdate> Updates(long engineOrderId, OrderState state, decimal lot)
    {
        if (state.ExecutionReportStatus == OrderExecutionReportStatus.ExecutionReportStatusRejected)
        {
            return [ExternalOrderUpdate.Reject(engineOrderId, RejectionReason.BrokerRejected, "T-Invest rejected the order.")];
        }

        long stageLots = state.Stages.Sum(stage => stage.Quantity);

        if (stageLots != state.LotsExecuted || state.Stages.Any(stage => stage.Price is null || !Roubles(stage.Price)) ||
            (state.ExecutedCommission is { } reported && !Roubles(reported)))
        {
            throw new TInvestOrderUnresolvedException();
        }

        decimal grossBase = state.LotsExecuted * lot;
        decimal grossQuote = state.Stages.Sum(stage => stage.Price.ToDecimal() * stage.Quantity * lot);
        decimal fee = state.ExecutedCommission?.ToDecimal() ?? 0m;
        bool pending = grossBase > 0m && fee == 0m;
        List<ExternalOrderUpdate> updates = [ExternalOrderUpdate.Accept(engineOrderId)];

        switch (state.ExecutionReportStatus)
        {
            case OrderExecutionReportStatus.ExecutionReportStatusFill:
                updates.Add(ExternalOrderUpdate.Final(engineOrderId, grossBase, grossQuote, fee, pending));

                break;
            case OrderExecutionReportStatus.ExecutionReportStatusCancelled:
                if (grossBase > 0m)
                {
                    updates.Add(ExternalOrderUpdate.Partial(engineOrderId, grossBase, grossQuote, fee, pending));
                }

                updates.Add(ExternalOrderUpdate.ConfirmCancel(engineOrderId));

                break;
            case OrderExecutionReportStatus.ExecutionReportStatusNew or OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill:
                if (grossBase > 0m)
                {
                    updates.Add(ExternalOrderUpdate.Partial(engineOrderId, grossBase, grossQuote, fee, pending));
                }

                break;
            default:
                throw new TInvestOrderUnresolvedException();
        }

        return updates;
    }

    /// <summary>
    ///     Whether the state can no longer change.
    /// </summary>
    public static bool IsFinal(OrderState state)
    {
        return state.ExecutionReportStatus is OrderExecutionReportStatus.ExecutionReportStatusFill or OrderExecutionReportStatus.ExecutionReportStatusRejected
            or OrderExecutionReportStatus.ExecutionReportStatusCancelled;
    }

    private static bool Roubles(MoneyValue money)
    {
        return string.Equals(money.Currency, "rub", StringComparison.OrdinalIgnoreCase);
    }
}