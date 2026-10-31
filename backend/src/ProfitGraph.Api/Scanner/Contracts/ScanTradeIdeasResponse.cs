namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A pair's trade cards now (ADR 0060): the price they start from, whether the owner has set up trading, and the proposals of every
///     timeframe; the costs they are sized with and how many proposals were left out for a stop nearer the entry than those costs
///     (ADR 0065); the backtests the odds are pooled from, newest first (ADR 0071, section 1; ADR 0073), empty without one.
/// </summary>
public sealed record ScanTradeIdeasResponse(
    string Venue,
    string Symbol,
    string? Price,
    bool HasSettings,
    IReadOnlyList<ScanTradeIdeaResponse> Ideas,
    ScanTradeCostsResponse Costs,
    int TightStops,
    IReadOnlyList<ScanCardOddsSourceResponse> OddsSources);