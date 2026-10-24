namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotCheckpointDb
{
    public Guid BotId { get; init; }
    public int SchemaVersion { get; init; }
    public string Checkpoint { get; init; } = string.Empty;
    public string AppliedParameters { get; init; } = "{}";
    public DateTimeOffset UpdatedAt { get; init; }
}