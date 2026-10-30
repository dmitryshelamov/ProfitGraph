using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.MarkPrices;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Application.Scanner.Trades;

/// <summary>
///     Checks a waiting scanner trade's entry right before it is sent (ADR 0067): the entry goes only while the price lies between the
///     trade's stop and target, so a limit entry cannot fill at once beyond the stop. The price is the venue's last trade when it is newer
///     than the bot's last closed candle, else that candle's close; one older than <see cref="ScanTradeOptions.EntryPriceMaxAge" /> is not
///     trusted. A refused entry fails the trade with <see cref="ScanTradeErrorCodes.EntryBeyondLevels" /> or
///     <see cref="ScanTradeErrorCodes.EntryPriceUnknown" />, and the tracker then stops its bot. Other orders and other bots pass.
/// </summary>
public sealed class ScanTradeEntryGuard(
    IScanTradeRepository trades,
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    ILiveMarkPriceRepository prices,
    ITransactionExecutor transactions,
    ScanTradeOptions options,
    TimeProvider clock) : IOrderSubmitGuard
{
    public async Task<string?> Refusal(BotId botId, OrderSnapshot order, CancellationToken cancellationToken)
    {
        if (order.Tag != LevelTradeStrategyBlockFactory.EntryTag || await bots.Get(botId, cancellationToken) is not { } bot)
        {
            return null;
        }

        DateTimeOffset now = clock.GetUtcNow();
        decimal? price = await Price(bot, now, cancellationToken);

        return await transactions.ExecuteAsync(
            async () =>
            {
                if (await trades.GetByBotForUpdate(botId, cancellationToken) is not { Status: ScanTradeStatus.Waiting } trade)
                {
                    return null;
                }

                if (price is { } known && trade.AllowsEntryAt(known))
                {
                    return null;
                }

                string code = price is null ? ScanTradeErrorCodes.EntryPriceUnknown : ScanTradeErrorCodes.EntryBeyondLevels;

                if (trade.Fail(code, now))
                {
                    await trades.Update(trade, cancellationToken);
                }

                return price is { } beyond
                    ? $"The price {ExactDecimalText.Format(beyond)} is no longer between the trade's stop {ExactDecimalText.Format(trade.Stop)} and target {ExactDecimalText.Format(trade.Target)}."
                    : "The pair's current price is unknown.";
            },
            cancellationToken);
    }

    /// <summary>
    ///     The pair's latest known price no older than <see cref="ScanTradeOptions.EntryPriceMaxAge" />, or null.
    /// </summary>
    private async Task<decimal?> Price(Bot bot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        BotCheckpoint? checkpoint = await checkpoints.Get(bot.Id, cancellationToken);
        LiveMarkPrice? ticker = await prices.Get(bot.Series.Venue, bot.Series.Symbol, cancellationToken);
        DateTimeOffset closedAt = checkpoint?.Engine.LastCandleCloseTime ?? DateTimeOffset.MinValue;

        if (ticker is not null && ticker.ObservedAt > closedAt && now - ticker.ObservedAt <= options.EntryPriceMaxAge)
        {
            return ticker.Price;
        }

        return checkpoint is not null && now - closedAt <= options.EntryPriceMaxAge ? checkpoint.Engine.LastClosedCandle.Close : null;
    }
}