using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     A venue's tradable pairs for picking one.
/// </summary>
public interface IInstrumentCatalog : IVenuePort
{
    /// <summary>
    ///     Finds pairs whose symbol or assets contain <paramref name="query" />, ignoring case; an empty query lists pairs.
    /// </summary>
    Task<IReadOnlyList<InstrumentInternal>> Search(string query, int limit, CancellationToken cancellationToken);
}