using System.Runtime.CompilerServices;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam.MarketData;

namespace ProfitGraph.Venues.Steam.LiveData;

/// <summary>
///     The display price of a Steam item: its highest buy order, what a sale would bring now, polled every
///     <see cref="SteamOptions.MarkPriceInterval" /> at quote priority (ADR 0022; display only, never read by the engine). An
///     item without buy orders yields nothing; a failure ends the stream, and the publisher opens it again later.
/// </summary>
internal sealed class SteamLiveMarkPriceSource(SteamMarketDataAccount account, IOptions<SteamOptions> options, TimeProvider clock) : ILiveMarkPriceSource
{
    public VenueId Venue => SteamVenue.VenueId;

    public async IAsyncEnumerable<LiveMarkPriceInternal> Subscribe(InstrumentSymbol symbol, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!SteamInstrument.TryParse(symbol.Value, out SteamInstrument? instrument))
        {
            throw new ArgumentException("The symbol is not a Steam item.", nameof(symbol));
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (await account.HighestBuyOrder(instrument, cancellationToken) is { } price)
            {
                yield return new LiveMarkPriceInternal(price, clock.GetUtcNow());
            }

            await Task.Delay(options.Value.MarkPriceInterval, clock, cancellationToken);
        }
    }
}