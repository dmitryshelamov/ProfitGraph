using System.Runtime.CompilerServices;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.Steam.MarketData;

namespace ProfitGraph.Venues.Steam.LiveData;

/// <summary>
///     Steam has no candle stream: after each close plus <see cref="SteamOptions.CandleCloseDelay" /> the source reads the
///     item's history again through the market-data account and yields the new closed candles in order. A period without a
///     candle while a later one exists breaks the contract of a gapless feed, so the subscription fails and the runner
///     restarts (ADR 0022).
/// </summary>
internal sealed class SteamLiveCandleSource(SteamMarketDataAccount account, IOptions<SteamOptions> options, TimeProvider clock) : ILiveCandleSource
{
    public VenueId Venue => SteamVenue.VenueId;

    public async IAsyncEnumerable<Candle> Subscribe(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset? afterOpen,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!SteamInstrument.TryParse(symbol.Value, out SteamInstrument? instrument))
        {
            throw new ArgumentException("The symbol is not a Steam item.", nameof(symbol));
        }

        TimeSpan delay = options.Value.CandleCloseDelay;
        DateTimeOffset next = afterOpen is { } last ? last + timeframe.Duration : timeframe.FloorToGrid(clock.GetUtcNow() - delay);

        while (!cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset end = timeframe.FloorToGrid(clock.GetUtcNow() - delay);

            if (next < end)
            {
                IReadOnlyList<Candle> candles = (await account.History(instrument, cancellationToken, clock.GetUtcNow())).For(timeframe);

                foreach (Candle candle in candles.Where(candle => candle.OpenTime >= next && candle.OpenTime < end))
                {
                    if (candle.OpenTime != next)
                    {
                        throw new InvalidOperationException($"Steam's history has no {timeframe.Key} candle at {next:O}.");
                    }

                    yield return candle;
                    next += timeframe.Duration;
                }
            }

            TimeSpan wait = end + timeframe.Duration + delay - clock.GetUtcNow();
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1), clock, cancellationToken);
        }
    }
}