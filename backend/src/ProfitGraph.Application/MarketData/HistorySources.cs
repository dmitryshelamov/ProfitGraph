using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData;

/// <summary>
///     The venues that offer public history: a venue without an <see cref="IHistoricalCandleSource" /> cannot download.
/// </summary>
public sealed class HistorySources(IEnumerable<IHistoricalCandleSource> sources)
{
    private readonly IReadOnlyList<IHistoricalCandleSource> _sources = sources.ToArray();

    public IHistoricalCandleSource? Find(VenueId venue)
    {
        return _sources.FirstOrDefault(source => source.Venue == venue);
    }
}