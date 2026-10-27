namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotDb
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid StrategyId { get; init; }
    public string? Name { get; init; }
    public Guid StrategyVersionId { get; init; }
    public Guid ExchangeCredentialId { get; init; }
    public string Venue { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public string Parameters { get; init; } = "{}";
    public decimal AllocationCap { get; init; }
    public decimal MaxOrderQuote { get; init; }
    public decimal DailyLossLimitQuote { get; init; }
    public decimal LimitFeeRate { get; init; }
    public decimal MarketFeeRate { get; init; }
    public DateTimeOffset FeeRatesReadAt { get; init; }
    public string? SellerFees { get; init; }
    public int VenueSettingsVersion { get; init; }
    public string VenueSettings { get; init; } = "{}";
    public bool AllowForeignOrders { get; init; }
    public string DesiredState { get; init; } = string.Empty;
    public string RuntimeStatus { get; init; } = string.Empty;
    public string? StatusReason { get; init; }
    public int Diagnostics { get; init; }
    public int RestartAttempts { get; init; }
    public DateTimeOffset? NextRestartAt { get; init; }
    public bool HasRun { get; init; }
    public DateTimeOffset? FirstStartedAt { get; init; }
    public DateTime? DayStartUtc { get; init; }
    public decimal? DayStartEquity { get; init; }
    public decimal? DayLastEquity { get; init; }
    public Guid? RejectedVersionId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}