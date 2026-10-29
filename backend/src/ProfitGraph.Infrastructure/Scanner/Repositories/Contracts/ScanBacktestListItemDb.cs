namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanBacktestListItemDb
{
    public Guid Id { get; init; }
    public string PresetName { get; init; } = string.Empty;
    public DateTimeOffset PeriodFrom { get; init; }
    public DateTimeOffset PeriodTo { get; init; }
    public Guid JobId { get; init; }
    public string? JobStatus { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int? Signals { get; init; }
}