using ProfitGraph.Engine.Accounting;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

public sealed record ScanTradeInternal(
    Guid Id,
    string Venue,
    string Symbol,
    ScanScenario Scenario,
    string LevelsTimeframe,
    decimal Entry,
    decimal Stop,
    decimal Target,
    decimal Amount,
    decimal Risk,
    Guid BotId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    ScanTradeStatus Status,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal? RealizedPnl,
    string? FailureCode,
    PositionDirection Direction = PositionDirection.Long);