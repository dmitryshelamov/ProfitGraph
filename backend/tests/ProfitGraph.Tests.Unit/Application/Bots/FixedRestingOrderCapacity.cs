using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Unit.Application.Bots;

internal sealed class FixedRestingOrderCapacity(VenueId venue, int limit) : IVenueRestingOrderCapacity
{
    public VenueId Venue => venue;

    public int MaxRestingOrders(InstrumentSymbol symbol)
    {
        return limit;
    }
}