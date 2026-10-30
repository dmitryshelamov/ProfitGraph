namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotPositionCloseDb
{
    public Guid Id { get; init; }
    public Guid BotId { get; init; }
    public bool StopAfter { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? CancelledOrders { get; init; }
    public long? EngineOrderId { get; init; }
    public string? FailureReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}