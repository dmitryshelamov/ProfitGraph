namespace ProfitGraph.Domain.Bots;

public interface IBotCheckpointRepository
{
    Task<BotCheckpoint?> Get(BotId botId, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores the checkpoint, replacing the bot's previous one.
    /// </summary>
    Task Save(BotCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the checkpoint of a forgotten run.
    /// </summary>
    Task Delete(BotId botId, CancellationToken cancellationToken);
}