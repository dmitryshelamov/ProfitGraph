namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Owner cancels of bot orders waiting for the bot's runner (ADR 0048); a bot has at most one pending.
/// </summary>
public interface IOrderCancelRequestRepository
{
    Task<OrderCancelRequest?> Get(OrderCancelRequestId id, CancellationToken cancellationToken);

    Task<OrderCancelRequest?> GetForUpdate(OrderCancelRequestId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's pending request, if any.
    /// </summary>
    Task<OrderCancelRequest?> GetPending(BotId botId, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds the request unless the bot already has a pending one; returns whether it was added.
    /// </summary>
    Task<bool> TryAdd(OrderCancelRequest request, CancellationToken cancellationToken);

    Task Update(OrderCancelRequest request, CancellationToken cancellationToken);
}