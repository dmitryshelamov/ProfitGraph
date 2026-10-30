namespace ProfitGraph.Domain.Bots.Fills;

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

    /// <summary>
    ///     At most <paramref name="limit" /> of the bot's fills made in the half-open UTC range [from, to), oldest first.
    /// </summary>
    Task<IReadOnlyList<BotFill>> ListFilled(BotId botId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     The fees of the bot's fills in each range, in quote and in the ranges' order: a fee paid in base is valued at its fill's
    ///     price, a fee paid in quote counts as it is.
    /// </summary>
    Task<IReadOnlyList<decimal>> QuoteFees(BotId botId, IReadOnlyList<BotFillRange> ranges, CancellationToken cancellationToken);
}