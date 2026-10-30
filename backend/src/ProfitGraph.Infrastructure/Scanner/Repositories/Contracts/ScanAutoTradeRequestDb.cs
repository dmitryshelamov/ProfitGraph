namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanAutoTradeRequestDb
{
    public Guid Id { get; init; }
    public Guid RuleId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public DateTimeOffset SnapshotAt { get; init; }
    public decimal? Price { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public Guid? TradeId { get; init; }
    public DateTimeOffset? HandledAt { get; init; }
}