namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     Declares that a venue has no anonymous market data: its history, search and rules are read with the caller's own
///     credential of the venue (ADR 0023, TC6). A venue without this port is read without one, and the runtime never
///     decrypts a bot's credential for its reads.
/// </summary>
public interface IVenueMarketDataAccess : IVenuePort;