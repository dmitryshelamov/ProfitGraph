namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanBacktestDb
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public string Venue { get; init; } = string.Empty;
    public string PresetName { get; init; } = string.Empty;
    public string FilterJson { get; init; } = "{}";
    public DateTimeOffset PeriodFrom { get; init; }
    public DateTimeOffset PeriodTo { get; init; }
    public Guid JobId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ResultJson { get; init; }
}