namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The cards of a pair now (ADR 0060): the snapshot's price they start from and whether the owner can create a trade.
/// </summary>
public sealed record ScanTradeIdeasInternal(
    string Venue,
    string Symbol,
    decimal? Price,
    DateTimeOffset? ComputedAt,
    bool HasSettings,
    bool HasLiveTrade,
    IReadOnlyList<ScanTradeIdeaInternal> Ideas);