namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     Which process polls Telegram for updates (ADR 0020): Telegram allows one <c>getUpdates</c> consumer, so the live
///     processes elect one by a lease. The lease keeps the next update offset, so a new leader continues where the last
///     stopped.
/// </summary>
public interface ITelegramPollerLeaseRepository
{
    /// <summary>
    ///     Takes or renews the lease for <paramref name="holderId" /> when it is free, expired or already held by it, and
    ///     returns the next update offset; null when another live holder has it.
    /// </summary>
    Task<long?> Acquire(Guid holderId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken);

    /// <summary>
    ///     Advances the update offset while the holder still holds the lease; returns whether it did.
    /// </summary>
    Task<bool> Advance(Guid holderId, long updateOffset, DateTimeOffset now, CancellationToken cancellationToken);
}