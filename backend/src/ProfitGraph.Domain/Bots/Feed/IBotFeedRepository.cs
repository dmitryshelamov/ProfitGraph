namespace ProfitGraph.Domain.Bots.Feed;

public interface IBotFeedRepository
{
    Task Add(BotFeedEntry entry, CancellationToken cancellationToken);

    /// <summary>
    ///     The bot's entries, newest first; <paramref name="before" /> continues after the last item of a previous page.
    /// </summary>
    Task<IReadOnlyList<SequencedBotFeedEntry>> List(BotId botId, long? before, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes entries that occurred before <paramref name="cutoff" />; returns how many.
    /// </summary>
    Task<int> DeleteOlderThan(DateTimeOffset cutoff, CancellationToken cancellationToken);
}