namespace ProfitGraph.Infrastructure.Common.Outbox;

internal sealed class OutboxMessageDb
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}