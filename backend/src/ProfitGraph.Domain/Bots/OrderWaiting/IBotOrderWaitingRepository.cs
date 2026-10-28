namespace ProfitGraph.Domain.Bots.OrderWaiting;

public interface IBotOrderWaitingRepository
{
    /// <summary>
    ///     The bot's waiting orders, oldest wait first.
    /// </summary>
    Task<IReadOnlyList<BotOrderWaiting>> List(BotId botId, CancellationToken cancellationToken);

    Task<BotOrderWaiting?> GetForUpdate(BotId botId, long engineOrderId, CancellationToken cancellationToken);

    Task Save(BotOrderWaiting waiting, CancellationToken cancellationToken);

    Task Delete(BotOrderWaiting waiting, CancellationToken cancellationToken);
}