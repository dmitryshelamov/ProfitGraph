using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Application.Backtesting.Converters;

public static class BacktestLedgerEntryInternalConverter
{
    public static BacktestLedgerEntryInternal ToInternal(this BacktestLedgerEntry entry)
    {
        return new BacktestLedgerEntryInternal(
            entry.Sequence,
            entry.Kind.ToString(),
            entry.Context.CandleIndex,
            entry.Context.Candle.OpenTime,
            entry.Context.Phase.ToString(),
            entry.Context.MarketPrice,
            entry.Order is { } order
                ? new BacktestOrderInternal(
                    order.Id,
                    order.Tag,
                    order.Side.ToString(),
                    order.Type.ToString(),
                    order.Quantity,
                    order.LimitPrice,
                    order.QuoteBudget,
                    order.Reservation,
                    order.Status.ToString(),
                    order.OriginNodeId)
                : null,
            entry.Fill is { } fill ? Fill(fill) : null,
            entry.Rejection is { } rejection
                ? new BacktestRejectionInternal(
                    rejection.Reason.ToString(),
                    rejection.Message,
                    rejection.Side.ToString(),
                    rejection.Type.ToString(),
                    rejection.Tag,
                    rejection.OrderId)
                : null);
    }

    private static BacktestFillInternal Fill(Fill fill)
    {
        return new BacktestFillInternal(
            fill.OrderId,
            fill.Tag,
            fill.Side.ToString(),
            fill.Type.ToString(),
            fill.Price,
            fill.GrossBase,
            fill.BaseChange,
            fill.QuoteChange,
            fill.Fee,
            fill.FeeAsset.ToString(),
            fill.RealizedPnl);
    }
}