namespace ProfitGraph.Domain.Bots.PositionCloses;

/// <summary>
///     Owners' position closes (ADR 0064); a bot has at most one that has not ended.
/// </summary>
public interface IBotPositionCloseRepository
{
    Task<BotPositionClose?> Get(BotPositionCloseId id, CancellationToken cancellationToken);

    Task<BotPositionClose?> GetForUpdate(BotPositionCloseId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's close that has not ended, if any.
    /// </summary>
    Task<BotPositionClose?> GetActive(BotId botId, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's newest close, whatever its status.
    /// </summary>
    Task<BotPositionClose?> GetLatest(BotId botId, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds the close unless the bot already has one that has not ended; returns whether it was added.
    /// </summary>
    Task<bool> TryAdd(BotPositionClose close, CancellationToken cancellationToken);

    Task Update(BotPositionClose close, CancellationToken cancellationToken);
}