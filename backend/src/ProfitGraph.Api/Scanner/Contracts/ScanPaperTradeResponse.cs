namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A rule's paper trade (ADR 0071, section 7): <c>Waiting</c>, <c>Open</c>, <c>Unfilled</c>, <c>Target</c>, <c>Stop</c> or
///     <c>Time</c>, its card, sizing, expected and actual R and the quote it made. Decimals are exact strings.
/// </summary>
public sealed record ScanPaperTradeResponse(
    Guid Id,
    string Symbol,
    string Timeframe,
    string Scenario,
    string Direction,
    string Entry,
    string Stop,
    string Target,
    string Amount,
    string Risk,
    string ExpectedR,
    DateTimeOffset CreatedAt,
    string Status,
    DateTimeOffset? EnteredAt,
    DateTimeOffset? ExitedAt,
    string? ResultR,
    string? Pnl);