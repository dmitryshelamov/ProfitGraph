using System.Runtime.CompilerServices;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.TInvest.MarketData;

namespace ProfitGraph.Venues.TInvest.LiveData;

/// <summary>
///     Live candles of a share by polling (ADR 0023, T9b amendment): after each interval closes plus
///     <see cref="TInvestOptions.CandleCloseGrace" />, the share's history up to the latest settled close is read with the
///     same completion rules as for backtests and its candles are published in order; closures are not published, the bot
///     reads them from history at its next candle. It reads with the bot's credential, which the runtime opens around the
///     subscription. A real candle that appears for an interval already published without trades is a history data
///     correction: the feed ends and the bot degrades for the owner.
/// </summary>
internal sealed class TInvestLiveCandleSource(TInvestHistoricalCandleSource history, IOptions<TInvestOptions> options, TimeProvider clock) : ILiveCandleSource
{
    public VenueId Venue => TInvestVenue.VenueId;

    public async IAsyncEnumerable<Candle> Subscribe(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset? afterOpen,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TimeSpan grace = options.Value.CandleCloseGrace;
        DateTimeOffset cursor = afterOpen is { } last ? last + timeframe.Duration : timeframe.FloorToGrid(clock.GetUtcNow() - grace);
        var quiet = new SortedSet<DateTimeOffset>();

        while (true)
        {
            DateTimeOffset settled = timeframe.FloorToGrid(clock.GetUtcNow() - grace);

            if (settled > cursor)
            {
                DateTimeOffset from = quiet.Count > 0 && quiet.Min < cursor ? quiet.Min : cursor;
                HistoricalCandlesInternal read = await history.Get(symbol, timeframe, from, settled, cancellationToken);

                if (read.Candles.Any(candle => !candle.NoTrades && quiet.Contains(candle.OpenTime)))
                {
                    throw new OrderGatewaySafetyException(
                        BotStatusReason.HistoryDataCorrection,
                        "T-Invest reported trades in an interval the bot already received without trades.");
                }

                foreach (Candle candle in read.Candles.Where(candle => candle.OpenTime >= cursor).OrderBy(candle => candle.OpenTime))
                {
                    if (candle.NoTrades)
                    {
                        quiet.Add(candle.OpenTime);
                    }

                    yield return candle;
                }

                cursor = settled;

                while (quiet.Count > options.Value.LiveRememberedQuietCandles)
                {
                    quiet.Remove(quiet.Min);
                }
            }

            // The interval opening at the cursor is settled once it closed and the grace passed.
            TimeSpan wait = cursor + timeframe.Duration + grace - clock.GetUtcNow();

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, clock, cancellationToken);
            }
        }
    }
}