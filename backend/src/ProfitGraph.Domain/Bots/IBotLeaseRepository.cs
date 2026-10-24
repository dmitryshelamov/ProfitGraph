namespace ProfitGraph.Domain.Bots;

public interface IBotLeaseRepository
{
    /// <summary>
    ///     Loads the bot's lease and locks it until the transaction ends; a bot that was never leased has none.
    /// </summary>
    Task<BotLease?> GetForUpdate(BotId botId, CancellationToken cancellationToken);

    Task Add(BotLease lease, CancellationToken cancellationToken);

    Task Update(BotLease lease, CancellationToken cancellationToken);
}