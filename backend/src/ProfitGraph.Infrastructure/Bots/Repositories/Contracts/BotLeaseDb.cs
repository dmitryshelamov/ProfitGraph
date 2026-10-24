namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotLeaseDb
{
    public Guid BotId { get; init; }
    public Guid HolderId { get; init; }
    public long Epoch { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool SubmissionsReady { get; init; }
}