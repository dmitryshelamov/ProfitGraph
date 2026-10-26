namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class ManualOrderRequestDb
{
    public Guid Id { get; init; }
    public Guid BotId { get; init; }
    public string Side { get; init; } = string.Empty;
    public string OrderType { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal? LimitPrice { get; init; }
    public string Status { get; init; } = string.Empty;
    public long? EngineOrderId { get; init; }
    public string? RejectionReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}