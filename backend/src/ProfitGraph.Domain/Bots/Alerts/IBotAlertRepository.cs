using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Domain.Bots.Alerts;

public interface IBotAlertRepository
{
    Task<BotAlert?> GetForUpdate(BotAlertId id, CancellationToken cancellationToken);

    /// <summary>
    ///     The newest alerts first: of one owner's bots, or of every bot when <paramref name="ownerId" /> is null.
    /// </summary>
    Task<IReadOnlyList<BotAlert>> List(UserId? ownerId, BotId? botId, bool unacknowledgedOnly, int limit, CancellationToken cancellationToken);

    Task Add(BotAlert alert, CancellationToken cancellationToken);

    Task Update(BotAlert alert, CancellationToken cancellationToken);
}