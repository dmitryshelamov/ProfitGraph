namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The cards of a pair now (ADR 0060): the price they start from, whether the owner has set up trading, and the proposals of every
///     timeframe.
/// </summary>
public sealed record ScanTradeIdeasInternal(string Venue, string Symbol, decimal? Price, bool HasSettings, IReadOnlyList<ScanTradeIdeaInternal> Ideas);