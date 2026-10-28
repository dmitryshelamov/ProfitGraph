namespace ProfitGraph.Domain.Bots.Positions;

public interface IBotPositionCycleRepository
{
    /// <summary>
    ///     The bot's open cycle, locked inside the current transaction.
    /// </summary>
    Task<BotPositionCycle?> GetOpenForUpdate(BotId botId, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's cycles, newest first.
    /// </summary>
    Task<IReadOnlyList<BotPositionCycle>> List(BotId botId, int limit, CancellationToken cancellationToken);

    Task Add(BotPositionCycle cycle, CancellationToken cancellationToken);

    Task Update(BotPositionCycle cycle, CancellationToken cancellationToken);
}