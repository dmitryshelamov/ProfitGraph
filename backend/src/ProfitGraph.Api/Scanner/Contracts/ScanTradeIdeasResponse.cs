namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A pair's trade cards now (ADR 0060): the price they start from, whether the owner has set up trading, and the proposals of every
///     timeframe.
/// </summary>
public sealed record ScanTradeIdeasResponse(string Venue, string Symbol, string? Price, bool HasSettings, IReadOnlyList<ScanTradeIdeaResponse> Ideas);