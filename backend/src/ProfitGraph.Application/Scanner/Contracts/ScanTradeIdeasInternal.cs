namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The cards of a pair now (ADR 0060): the price they start from, whether the owner has set up trading, and the proposals of every
///     timeframe; the costs they are sized with, and how many proposals were left out because their stop is nearer the entry than those
///     costs (ADR 0065).
/// </summary>
public sealed record ScanTradeIdeasInternal(
    string Venue,
    string Symbol,
    decimal? Price,
    bool HasSettings,
    IReadOnlyList<ScanTradeIdeaInternal> Ideas,
    ScanTradeCostsInternal Costs,
    int TightStops);