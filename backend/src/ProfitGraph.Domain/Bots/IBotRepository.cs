using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Bots change under a row lock: a writer loads the bot with <see cref="GetForUpdate" /> in its transaction, so the
///     owner's commands and the runtime never overwrite each other.
/// </summary>
public interface IBotRepository
{
    Task<Bot?> Get(BotId id, CancellationToken cancellationToken);

    /// <summary>
    ///     Loads the bot and locks its row until the transaction ends.
    /// </summary>
    Task<Bot?> GetForUpdate(BotId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The owner's bots, newest first.
    /// </summary>
    Task<IReadOnlyList<Bot>> ListByOwner(UserId ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BotId>> ListDesiredRunning(CancellationToken cancellationToken);

    Task Add(Bot bot, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes a bot loaded with <see cref="GetForUpdate" /> in the current transaction.
    /// </summary>
    Task Update(Bot bot, CancellationToken cancellationToken);
}