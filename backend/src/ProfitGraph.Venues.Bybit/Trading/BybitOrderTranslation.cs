using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Turns Bybit's cumulative order state into engine updates in the order the engine accepts: acceptance, then the
///     cumulative fill or the final state. A buy's fee must be in the base asset and a sell's in the quote asset
///     (Profit Fox Designer ADR 0055); another fee asset cannot be accounted exactly.
/// </summary>
internal static class BybitOrderTranslation
{
    /// <summary>
    ///     The updates of a bot's order in its market: a USDT perpetual pays every fee in quote (ADR 0061).
    /// </summary>
    public static IReadOnlyList<ExternalOrderUpdate> Updates(long engineOrderId, BybitSpotOrder order, BybitBotContext context)
    {
        return Updates(engineOrderId, order, context.Instrument.BaseAsset, context.Instrument.QuoteAsset, context.Market.IsLinear);
    }

    public static IReadOnlyList<ExternalOrderUpdate> Updates(long engineOrderId, BybitSpotOrder order, string baseAsset, string quoteAsset, bool quoteFees = false)
    {
        if (order.Status == "Rejected")
        {
            return [ExternalOrderUpdate.Reject(engineOrderId, RejectionReason.BrokerRejected, "Bybit rejected the order.")];
        }

        string feeAsset = order.Side == "Buy" && !quoteFees ? baseAsset : quoteAsset;

        if (order.CumulativeFees.Any(pair => pair.Key != feeAsset && pair.Value != 0m))
        {
            throw new BybitUnsupportedFeeAssetException();
        }

        decimal fee = order.CumulativeFees.GetValueOrDefault(feeAsset);
        List<ExternalOrderUpdate> updates = [ExternalOrderUpdate.Accept(engineOrderId)];

        switch (order.Status)
        {
            case "Filled":
                updates.Add(ExternalOrderUpdate.Final(engineOrderId, order.CumulativeBase, order.CumulativeQuote, fee));

                break;
            case "Cancelled" or "PartiallyFilledCanceled" or "Deactivated":
                if (order.CumulativeBase > 0m)
                {
                    updates.Add(ExternalOrderUpdate.Partial(engineOrderId, order.CumulativeBase, order.CumulativeQuote, fee));
                }

                updates.Add(ExternalOrderUpdate.ConfirmCancel(engineOrderId));

                break;
            default:
                if (order.CumulativeBase > 0m)
                {
                    updates.Add(ExternalOrderUpdate.Partial(engineOrderId, order.CumulativeBase, order.CumulativeQuote, fee));
                }

                break;
        }

        return updates;
    }
}
