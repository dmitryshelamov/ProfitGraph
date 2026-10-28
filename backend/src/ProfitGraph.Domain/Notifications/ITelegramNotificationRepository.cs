namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     The queue of Telegram notifications (ADR 0042). Several senders share it: <see cref="LockDue" /> skips rows another
///     transaction holds.
/// </summary>
public interface ITelegramNotificationRepository
{
    Task Add(TelegramNotification notification, CancellationToken cancellationToken);

    /// <summary>
    ///     Locks up to <paramref name="limit" /> notifications that may be claimed at <paramref name="now" />, oldest first,
    ///     skipping rows another transaction holds. Only inside a transaction.
    /// </summary>
    Task<IReadOnlyList<TelegramNotification>> LockDue(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    Task<TelegramNotification?> GetForUpdate(TelegramNotificationId id, CancellationToken cancellationToken);

    Task Update(TelegramNotification notification, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes finished notifications created before <paramref name="cutoff" />; returns how many.
    /// </summary>
    Task<int> DeleteFinishedBefore(DateTimeOffset cutoff, CancellationToken cancellationToken);
}