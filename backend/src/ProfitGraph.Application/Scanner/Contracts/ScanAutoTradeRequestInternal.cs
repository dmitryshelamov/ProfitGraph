namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A signal of a rule and what came of it (ADR 0071, section 6): <c>Pending</c>, <c>Created</c>, <c>Paper</c> or a skip reason, the
///     error of a refused trade and the trade made.
/// </summary>
public sealed record ScanAutoTradeRequestInternal(
    Guid Id,
    string Symbol,
    DateTimeOffset SnapshotAt,
    decimal? Price,
    string Status,
    string? ErrorCode,
    Guid? TradeId,
    DateTimeOffset? HandledAt);