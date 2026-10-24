namespace ProfitGraph.Application.MarketData;

public static class InstrumentRulesErrorCodes
{
    /// <summary>
    ///     The venue has no such tradable instrument, or offers no instrument data at all.
    /// </summary>
    public const string InvalidRequest = "instrument_rules_invalid_request";

    public const string InvalidProviderData = "instrument_rules_invalid_provider_data";
    public const string ProviderUnavailable = "instrument_rules_provider_unavailable";
}