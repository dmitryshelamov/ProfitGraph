namespace ProfitGraph.Application.MarketData;

public static class HistoricalDataErrorCodes
{
    /// <summary>
    ///     The venue has no history for the range, or the venue offers no history at all.
    /// </summary>
    public const string Unavailable = "historical_data_unavailable";

    public const string Conflict = "historical_data_conflict";
    public const string InvalidProviderData = "invalid_provider_data";
    public const string InvalidStoredData = "invalid_stored_data";
    public const string ProviderUnavailable = "provider_unavailable";

    /// <summary>
    ///     The venue rejected the request itself, for example an unknown symbol.
    /// </summary>
    public const string InvalidRequest = "historical_data_invalid_request";
}