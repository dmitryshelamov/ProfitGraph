namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanAutoTradeRuleDb
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public Guid PresetId { get; init; }
    public string Venue { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Scenario { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public decimal MinExpectedR { get; init; }
    public decimal? MinChance { get; init; }
    public int MaxOpenTrades { get; init; }
    public int MaxTradesPerDay { get; init; }
    public decimal DailyLossLimit { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string? ProofJson { get; init; }
    public DateTime? PausedOn { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
    public string Pairs { get; init; } = "{}";
    public DateTimeOffset ModeSince { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}