namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     The stored series and the range covered by the imported candles: from the first open to the last close.
/// </summary>
public sealed record CandleImportInternal(string Venue, string Symbol, string Timeframe, int Accepted, DateTimeOffset From, DateTimeOffset To);