namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanPaperTradeDb
{
    public Guid Id { get; init; }
    public Guid RuleId { get; init; }
    public string Venue { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public string Scenario { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal Entry { get; init; }
    public decimal Stop { get; init; }
    public decimal Target { get; init; }
    public decimal RewardRisk { get; init; }
    public int EntryWaitMinutes { get; init; }
    public decimal Amount { get; init; }
    public decimal Risk { get; init; }
    public decimal LimitFeeRate { get; init; }
    public decimal MarketFeeRate { get; init; }
    public decimal Spread { get; init; }
    public decimal ExpectedR { get; init; }
    public decimal? Chance { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastMinute { get; init; }
    public int Waited { get; init; }
    public DateTimeOffset? EnteredAt { get; init; }
    public int Held { get; init; }
    public string? ExitReason { get; init; }
    public bool? Filled { get; init; }
    public decimal? ResultR { get; init; }
    public DateTimeOffset? ExitedAt { get; init; }
}