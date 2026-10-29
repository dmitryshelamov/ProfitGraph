namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A pair's trade cards now (ADR 0060), the snapshot price they start from, whether the owner has set up trading and whether a
///     trade on the pair is still live.
/// </summary>
public sealed record ScanTradeIdeasResponse(
    string Venue,
    string Symbol,
    string? Price,
    DateTimeOffset? ComputedAt,
    bool HasSettings,
    bool HasLiveTrade,
    IReadOnlyList<ScanTradeIdeaResponse> Ideas);