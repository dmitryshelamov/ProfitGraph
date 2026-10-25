using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     What a root changed in the bot's trading record (ADR 0021): the fill a venue update caused, as the change since the
///     order's previous fill, with the asset its fee was paid in, a late commission the venue reported for already accounted
///     fills (ADR 0023, TC4), and the position opening or becoming flat.
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