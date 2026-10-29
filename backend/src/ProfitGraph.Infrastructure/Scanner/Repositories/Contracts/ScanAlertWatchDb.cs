namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

internal sealed class ScanAlertWatchDb
{
    public Guid PresetId { get; init; }
    public Guid OwnerId { get; init; }
    public DateTimeOffset EnabledAt { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
    public string Pairs { get; init; } = "{}";
}