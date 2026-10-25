using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     T-Invest has no anonymous API: every market data read uses the caller's T-Invest credential (ADR 0023, TC6).
/// </summary>
internal sealed class TInvestMarketDataAccess : IVenueMarketDataAccess
{
    public VenueId Venue => TInvestVenue.VenueId;
}