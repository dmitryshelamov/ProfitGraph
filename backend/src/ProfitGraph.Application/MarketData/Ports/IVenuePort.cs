using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     A capability one venue module implements (ADR 0003): its presence is the capability.
/// </summary>
public interface IVenuePort
{
    VenueId Venue { get; }
}