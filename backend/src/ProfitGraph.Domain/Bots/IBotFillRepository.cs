namespace ProfitGraph.Domain.Bots;

public interface IBotFillRepository
{
    /// <summary>
    ///     Records the fill unless the same execution of its order is already recorded; returns whether it was new.
    /// </summary>
    Task<bool> TryAdd(BotFill fill, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's fills, newest first, each with its position in the bot's record; <paramref name="before" /> continues
    ///     after the last item of a previous page.
    /// </summary>
    Task<IReadOnlyList<SequencedBotFill>> List(BotId botId, long? before, int limit, CancellationToken cancellationToken);
}