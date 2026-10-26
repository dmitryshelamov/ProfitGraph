using ProfitGraph.Domain.MarketData;

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
    ///     Reads an intent without locking it, so a writer can lock its bot's lease and bot first (ADR 0016: lease, bot, intent).
    /// </summary>
    Task<OrderIntent?> Get(OrderIntentId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's intents in the order they were written.
    /// </summary>
    Task<IReadOnlyList<OrderIntent>> ListByBot(BotId botId, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's submit for an engine order, read without a lock, or null.
    /// </summary>
    Task<OrderIntent?> GetSubmit(BotId botId, long engineOrderId, CancellationToken cancellationToken);

    Task Update(OrderIntent intent, CancellationToken cancellationToken);

    /// <summary>
    ///     Intents that may be due for delivery by <paramref name="holderId" />, in delivery order: a submit only of a bot
    ///     the holder leases, a cancel also of a bot no runtime leases. An intent waits behind earlier undelivered intents
    ///     of its bot, except that a cancel passes submits of other orders, and every submit of a bot that should stop.
    ///     Domain rules decide whether each is claimed.
    ///     Only intents of bots on <paramref name="venues" />, the venues this process can deliver to, are listed, so an
    ///     intent no gateway here can send never holds the others back.
    /// </summary>
    Task<IReadOnlyList<OrderIntentId>> ListDeliveryCandidates(
        LeaseHolderId holderId,
        IReadOnlyList<VenueId> venues,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Bots that should stop but still have submits waiting.
    /// </summary>
    Task<IReadOnlyList<BotId>> ListStoppedBotsWithWaitingSubmits(CancellationToken cancellationToken);
}