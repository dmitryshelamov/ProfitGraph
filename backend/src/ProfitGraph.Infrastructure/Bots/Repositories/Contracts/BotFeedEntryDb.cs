namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotFeedEntryDb
{
    public Guid Id { get; init; }
    public long Sequence { get; init; }
    public Guid BotId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Details { get; init; } = "{}";
    public DateTimeOffset OccurredAt { get; init; }
}