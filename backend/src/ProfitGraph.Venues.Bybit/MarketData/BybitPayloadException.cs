namespace ProfitGraph.Venues.Bybit.MarketData;

/// <summary>
///     A Bybit response whose values cannot be read exactly.
/// </summary>
internal sealed class BybitPayloadException(string message) : Exception(message);