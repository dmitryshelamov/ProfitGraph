namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Owner manual orders waiting for the bot's runner (Profit Fox Designer ADR 0091); a bot has at most one pending.
/// </summary>
public interface IManualOrderRequestRepository
{
    Task<ManualOrderRequest?> Get(ManualOrderRequestId id, CancellationToken cancellationToken);

    Task<ManualOrderRequest?> GetForUpdate(ManualOrderRequestId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's pending request, if any.
    /// </summary>
    Task<ManualOrderRequest?> GetPending(BotId botId, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds the request unless the bot already has a pending one; returns whether it was added.
    /// </summary>
    Task<bool> TryAdd(ManualOrderRequest request, CancellationToken cancellationToken);

    Task Update(ManualOrderRequest request, CancellationToken cancellationToken);
}