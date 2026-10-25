using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A durable notice about a bot for its owner (Profit Fox Designer ADR 0056). It names a kind and a reason, never
///     credentials or venue payloads, and stays until the owner acknowledges it.
/// </summary>
public sealed class BotAlert : AggregateRoot
{
    public BotAlertId Id { get; }
    public BotId BotId { get; }
    public UserId OwnerId { get; }
    public BotAlertKind Kind { get; }

    /// <summary>
    ///     The status or rejection reason behind the alert, by name, or null when the kind says it all.
    /// </summary>
    public string? Reason { get; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }

    private BotAlert(BotAlertId id, BotId botId, UserId ownerId, BotAlertKind kind, string? reason, DateTimeOffset createdAt, DateTimeOffset? acknowledgedAt)
    {
        Id = id;
        BotId = botId;
        OwnerId = ownerId;
        Kind = kind;
        Reason = reason;
        CreatedAt = createdAt;
        AcknowledgedAt = acknowledgedAt;
    }

    public static BotAlert Raise(BotAlertId id, BotId botId, UserId ownerId, BotAlertKind kind, string? reason, DateTimeOffset now)
    {
        return new BotAlert(id, botId, ownerId, kind, reason, now, acknowledgedAt: null);
    }

    public static BotAlert Restore(
        BotAlertId id,
        BotId botId,
        UserId ownerId,
        BotAlertKind kind,
        string? reason,
        DateTimeOffset createdAt,
        DateTimeOffset? acknowledgedAt)
    {
        return new BotAlert(id, botId, ownerId, kind, reason, createdAt, acknowledgedAt);
    }

    /// <summary>
    ///     Marks the alert read; returns whether it was unread.
    /// </summary>
    public bool Acknowledge(DateTimeOffset now)
    {
        if (AcknowledgedAt is not null)
        {
            return false;
        }

        AcknowledgedAt = now;

        return true;
    }
}