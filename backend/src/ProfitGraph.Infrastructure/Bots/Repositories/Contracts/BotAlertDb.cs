namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotAlertDb
{
    public Guid Id { get; init; }
    public Guid BotId { get; init; }
    public Guid OwnerId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }
}