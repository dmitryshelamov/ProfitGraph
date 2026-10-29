using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     How many resting Spot orders one bot may keep on a pair (ADR 0058): <see cref="BybitOptions.MaxRestingOrdersPerBot" />.
///     The number is ours, below what Bybit allows an account; check it against Bybit's documentation before a grid trades.
/// </summary>
internal sealed class BybitRestingOrderCapacity(BybitOptions options, BybitMarket? market = null) : IVenueRestingOrderCapacity
{
    private readonly BybitMarket _market = market ?? BybitMarket.Spot;

    public VenueId Venue => _market.Venue;

    public int MaxRestingOrders(InstrumentSymbol symbol)
    {
        return options.MaxRestingOrdersPerBot;
    }
}