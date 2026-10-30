namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A signal of a rule and what came of it (ADR 0071, section 6): <c>Pending</c>, <c>Created</c>, <c>Paper</c> or a skip reason.
/// </summary>
public sealed record ScanAutoTradeRequestResponse(
    Guid Id,
    string Symbol,
    DateTimeOffset SnapshotAt,
    string? Price,
    string Status,
    string? ErrorCode,
    Guid? TradeId,
    DateTimeOffset? HandledAt);