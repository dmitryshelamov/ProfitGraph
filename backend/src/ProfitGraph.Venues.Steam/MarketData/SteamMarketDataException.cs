namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     Steam's market data could not be read or understood. The message names the cause, never provider text.
/// </summary>
internal sealed class SteamMarketDataException(bool unavailable, string message) : Exception(message)
{
    /// <summary>
    ///     True when Steam could not be asked now (disabled, held back, rate limited, unreachable); false when it answered
    ///     something unusable.
    /// </summary>
    public bool Unavailable { get; } = unavailable;
}