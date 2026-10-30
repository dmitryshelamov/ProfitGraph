using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A rule's paper trade (ADR 0071, section 7): its card, sizing and expected R, where it stands (<c>Waiting</c>, <c>Open</c>,
///     <c>Unfilled</c> or how it left) and its result.
/// </summary>
public sealed record ScanPaperTradeInternal(
    Guid Id,
    string Symbol,
    string Timeframe,
    ScanScenario Scenario,
    PositionDirection Direction,
    decimal Entry,
    decimal Stop,
    decimal Target,
    decimal Amount,
    decimal Risk,
    decimal ExpectedR,
    DateTimeOffset CreatedAt,
    string Status,
    DateTimeOffset? EnteredAt,
    DateTimeOffset? ExitedAt,
    decimal? ResultR,
    decimal? Pnl);