namespace ProfitGraph.Domain.Bots;

public interface IOrderIntentRepository
{
    /// <summary>
    ///     Adds the intent unless the bot already has one of the same kind for the same engine order, which makes saving
    ///     a root twice harmless. Returns whether it was added.
    /// </summary>
    Task<bool> TryAdd(OrderIntent intent, CancellationToken cancellationToken);

    Task<OrderIntent?> GetForUpdate(OrderIntentId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's intents in the order they were written.
    /// </summary>
    Task<IReadOnlyList<OrderIntent>> ListByBot(BotId botId, CancellationToken cancellationToken);

    Task Update(OrderIntent intent, CancellationToken cancellationToken);
}