using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     How many resting orders one bot may keep on an instrument of the venue (ADR 0058, section 9). Only a venue with this
///     port runs a grid: the grid needs its buys and every bought level's sale at once.
/// </summary>
public interface IVenueRestingOrderCapacity : IVenuePort
{
    int MaxRestingOrders(InstrumentSymbol symbol);
}