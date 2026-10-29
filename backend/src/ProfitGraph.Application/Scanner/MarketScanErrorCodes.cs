namespace ProfitGraph.Application.Scanner;

public static class MarketScanErrorCodes
{
    public const string InvalidProviderData = "market_scan_invalid_provider_data";
    public const string ProviderUnavailable = "market_scan_provider_unavailable";

    /// <summary>
    ///     The venue banned the server's address for too many requests; the scanner sends none until the pause ends.
    /// </summary>
    public const string RequestsPaused = "market_scan_requests_paused";
}