using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData;

/// <summary>
///     The registered implementations of one venue port; a venue without one lacks the capability.
/// </summary>
public sealed class VenuePorts<TPort>(IEnumerable<TPort> ports)
    where TPort : IVenuePort
{
    private readonly IReadOnlyList<TPort> _ports = ports.ToArray();

    public TPort? Find(VenueId venue)
    {
        return _ports.FirstOrDefault(port => port.Venue == venue);
    }
}