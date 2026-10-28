using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Bots.MarkPrices;

public interface ILiveMarkPriceRepository
{
    /// <summary>
    ///     Stores the observation unless a later one is stored, and signals it; returns whether it was stored.
    /// </summary>
    Task<bool> Store(LiveMarkPrice price, CancellationToken cancellationToken);

    Task<LiveMarkPrice?> Get(VenueId venue, InstrumentSymbol symbol, CancellationToken cancellationToken);

    /// <summary>
    ///     The price a signal named.
    /// </summary>
    Task<LiveMarkPrice?> GetBySignal(Guid signal, CancellationToken cancellationToken);
}