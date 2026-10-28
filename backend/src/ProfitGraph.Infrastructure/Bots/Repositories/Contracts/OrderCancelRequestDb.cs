namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class OrderCancelRequestDb
{
    public Guid Id { get; init; }
    public Guid BotId { get; init; }
    public long EngineOrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? RejectionReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}