using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     What a root changed in the bot's trading record (ADR 0021): the fill a venue update caused, as the change since the
///     order's previous fill, with the asset its fee was paid in, a late commission the venue reported for already accounted
///     fills (ADR 0023, TC4), an order filled completely (ADR 0042), and the position opening or becoming flat.
/// </summary>
internal static class BotRootAccounting
{
    public static IReadOnlyList<BotFill> Fills(
        BotId botId,
        GraphEngineCheckpoint? before,
        GraphEngineCheckpoint after,
        ExternalOrderUpdate? update,
        FeeAsset buyFeeAsset,
        DateTimeOffset now)
    {
        if (update is not { Kind: ExternalOrderUpdateKind.PartialFill or ExternalOrderUpdateKind.FinalFill, CumulativeGrossBase: { } cumulativeBase } ||
            before?.Account.Orders.FirstOrDefault(candidate => candidate.Id == update.OrderId) is not { } order ||
            (before.Account.QuoteTotal == after.Account.QuoteTotal && before.Account.BaseTotal == after.Account.BaseTotal))
        {
            return [];
        }

        ExternalOrderProgressCheckpoint? previous = before.Account.Progress.FirstOrDefault(progress => progress.OrderId == update.OrderId);
        decimal grossBase = cumulativeBase - (previous?.GrossBase ?? 0m);
        decimal grossQuote = update.CumulativeGrossQuote.GetValueOrDefault() - (previous?.GrossQuote ?? 0m);
        decimal fee = Math.Max(update.CumulativeFee.GetValueOrDefault() - (previous?.Fee ?? 0m), val2: 0m);

        return grossBase > 0m && grossQuote > 0m
            ?
            [
                BotFill.Record(
                    BotFillId.New(),
                    botId,
                    order.Id,
                    order.Side,
                    order.Tag,
                    grossBase,
                    grossQuote,
                    fee,
                    cumulativeBase,
                    now,
                    order.Side == OrderSide.Buy ? buyFeeAsset : FeeAsset.Quote)
            ]
            : [];
    }

    /// <summary>
    ///     The totals of the order a root filled completely (ADR 0042): the root applied a final fill to an order the checkpoint
    ///     before it still held and the checkpoint after it no longer holds. Gross amounts and the fee are the update's cumulative
    ///     values, as the bot's fills record them; a sell's realized profit adds the root's change to what the order's earlier
    ///     fills realized. Nothing for any other root.
    /// </summary>
    public static BotOrderFill? OrderFilled(GraphEngineCheckpoint? before, GraphEngineCheckpoint after, ExternalOrderUpdate? update, InstrumentRules rules)
    {
        if (update is not { Kind: ExternalOrderUpdateKind.FinalFill, CumulativeGrossBase: > 0m and var grossBase, CumulativeGrossQuote: > 0m and var grossQuote } ||
            before?.Account.Orders.FirstOrDefault(candidate => candidate.Id == update.OrderId) is not { } order ||
            after.Account.Orders.Any(candidate => candidate.Id == update.OrderId))
        {
            return null;
        }

        ExternalOrderProgressCheckpoint? previous = before.Account.Progress.FirstOrDefault(progress => progress.OrderId == update.OrderId);
        decimal fee = Math.Max(update.CumulativeFee.GetValueOrDefault(), previous?.Fee ?? 0m);
        decimal? realized = order.Side == OrderSide.Sell
            ? (previous?.RealizedPnl ?? 0m) + after.Account.RealizedPnl - before.Account.RealizedPnl
            : null;

        return new BotOrderFill(
            order.Id,
            order.Side,
            order.Tag,
            grossBase,
            grossQuote,
            fee,
            order.Side == OrderSide.Buy ? rules.BuyFeeAsset : FeeAsset.Quote,
            realized,
            rules.QuantityStep,
            rules.PriceTick,
            rules.QuotePrecision);
    }

    /// <summary>
    ///     The order's cumulative fee before and after a fee correction the engine applied, from the fee receipts of both
    ///     checkpoints; nothing when the update was no correction or changed nothing.
    /// </summary>
    public static IReadOnlyList<BotFeeCorrection> FeeCorrections(
        BotId botId,
        GraphEngineCheckpoint? before,
        GraphEngineCheckpoint after,
        ExternalOrderUpdate? update,
        DateTimeOffset now)
    {
        if (update is not { Kind: ExternalOrderUpdateKind.FeeCorrection } ||
            before?.Account.FeeReceipts.FirstOrDefault(receipt => receipt.OrderId == update.OrderId) is not { } previous ||
            after.Account.FeeReceipts.FirstOrDefault(receipt => receipt.OrderId == update.OrderId) is not { } corrected ||
            corrected.Fee <= previous.Fee)
        {
            return [];
        }

        return [BotFeeCorrection.Record(BotFeeCorrectionId.New(), botId, update.OrderId, previous.Fee, corrected.Fee, now)];
    }

    public static BotRootPosition? Position(GraphEngineCheckpoint? before, GraphEngineCheckpoint after, DateTimeOffset now)
    {
        return (before?.PositionOpenedAt, after.PositionOpenedAt) switch
        {
            (null, { } openedAt) => new BotRootPosition(BotPositionChange.Opened, openedAt, after.Account.RealizedPnl),
            ({ }, null) => new BotRootPosition(BotPositionChange.Closed, now, after.Account.RealizedPnl),
            _ => null
        };
    }
}