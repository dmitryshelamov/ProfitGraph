using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     The leverage of a bot on a margin venue (ADR 0061), read from the venue module's own bot settings; the engine reserves an
///     opening order's notional divided by it. A venue without this port is not a margin venue.
/// </summary>
public interface IVenueLeverage : IVenuePort
{
    decimal Leverage(BotVenueSettings settings);
}