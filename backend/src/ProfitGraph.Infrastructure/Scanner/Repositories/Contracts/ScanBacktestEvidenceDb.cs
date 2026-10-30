namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanBacktestEvidenceDb
{
    public Guid Id { get; init; }
    public string PresetName { get; init; } = string.Empty;
    public string FilterJson { get; init; } = "{}";
    public DateTimeOffset PeriodFrom { get; init; }
    public DateTimeOffset PeriodTo { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public decimal? MarketChange { get; init; }
}