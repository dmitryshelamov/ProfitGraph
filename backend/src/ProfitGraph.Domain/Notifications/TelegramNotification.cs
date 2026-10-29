using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Alerts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     A message waiting for its owner's Telegram chat (ADR 0042): about a bot, or a scanner preset's alert (ADR 0059). A sender claims it for <see cref="ClaimDuration" />,
///     sends it outside any transaction and records the outcome only while its claim is the current one, so a sender that
///     died leaves it to another after the claim expires. Delivery is at least once. A temporary failure waits by
///     <see cref="TelegramRetrySchedule" />; a notification older than <see cref="MaxAge" /> is skipped instead of sent.
/// </summary>
public sealed class TelegramNotification : AggregateRoot
{
    private const int MaxErrorLength = 200;
    public static readonly TimeSpan ClaimDuration = TimeSpan.FromSeconds(45);

    /// <summary>
    ///     How long a finished notification is kept.
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private static readonly TimeSpan NotificationMaxAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan AlertMaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan ScanAlertMaxAge = TimeSpan.FromMinutes(15);

    public TelegramNotificationId Id { get; }
    public UserId OwnerId { get; }

    /// <summary>
    ///     The bot the message is about; null for a <see cref="TelegramNotificationKind.ScanAlert" />.
    /// </summary>
    public BotId? BotId { get; }

    /// <summary>
    ///     The preset a <see cref="TelegramNotificationKind.ScanAlert" /> reports; null for other kinds.
    /// </summary>
    public ScanPresetId? PresetId { get; }

    public TelegramNotificationKind Kind { get; }

    /// <summary>
    ///     The alert an <see cref="TelegramNotificationKind.Alert" /> reports; null for other kinds.
    /// </summary>
    public BotAlertId? AlertId { get; }

    /// <summary>
    ///     What the message needs, as invariant strings: reasons by name, amounts as exact decimals. The notification keeps its
    ///     own copy.
    /// </summary>
    public IReadOnlyDictionary<string, string> Details { get; }

    public DateTimeOffset CreatedAt { get; }
    public TelegramNotificationStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public TelegramClaimId? ClaimId { get; private set; }
    public DateTimeOffset? ClaimedUntil { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    ///     How old a notification may be when it is sent: a day for an alert, 15 minutes for a scanner alert, an hour for anything
    ///     else, so a sender that comes back after downtime does not flood the chat.
    /// </summary>
    public TimeSpan MaxAge => Kind switch
    {
        TelegramNotificationKind.Alert => AlertMaxAge,
        TelegramNotificationKind.ScanAlert => ScanAlertMaxAge,
        _ => NotificationMaxAge
    };

    private TelegramNotification(
        TelegramNotificationId id,
        UserId ownerId,
        BotId? botId,
        ScanPresetId? presetId,
        TelegramNotificationKind kind,
        BotAlertId? alertId,
        IReadOnlyDictionary<string, string> details,
        DateTimeOffset createdAt,
        TelegramNotificationStatus status,
        int attempts,
        DateTimeOffset nextAttemptAt,
        TelegramClaimId? claimId,
        DateTimeOffset? claimedUntil,
        DateTimeOffset? sentAt,
        string? lastError)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (kind == TelegramNotificationKind.Alert != alertId.HasValue)
        {
            throw new ArgumentException("An alert notification names its alert, and only it does.", nameof(alertId));
        }

        if (kind == TelegramNotificationKind.ScanAlert != presetId.HasValue || kind == TelegramNotificationKind.ScanAlert == botId.HasValue)
        {
            throw new ArgumentException("A scanner alert names its preset and no bot; every other notification names its bot.", nameof(presetId));
        }

        Id = id;
        OwnerId = ownerId;
        BotId = botId;
        PresetId = presetId;
        Kind = kind;
        AlertId = alertId;
        Details = new Dictionary<string, string>(details, StringComparer.Ordinal).AsReadOnly();
        CreatedAt = createdAt;
        Status = status;
        Attempts = attempts;
        NextAttemptAt = nextAttemptAt;
        ClaimId = claimId;
        ClaimedUntil = claimedUntil;
        SentAt = sentAt;
        LastError = lastError;
    }

    /// <summary>
    ///     A new notification of an event that happened at <paramref name="at" />, due at once.
    /// </summary>
    public static TelegramNotification Queue(
        TelegramNotificationId id,
        UserId ownerId,
        BotId botId,
        TelegramNotificationKind kind,
        BotAlertId? alertId,
        IReadOnlyDictionary<string, string> details,
        DateTimeOffset at)
    {
        return new TelegramNotification(
            id,
            ownerId,
            botId,
            presetId: null,
            kind,
            alertId,
            details,
            at,
            TelegramNotificationStatus.Pending,
            attempts: 0,
            at,
            claimId: null,
            claimedUntil: null,
            sentAt: null,
            lastError: null);
    }

    /// <summary>
    ///     A new alert of a scanner preset (ADR 0059) about pairs that entered its result in the snapshot computed at
    ///     <paramref name="at" />, due at once.
    /// </summary>
    public static TelegramNotification QueueScanAlert(
        TelegramNotificationId id,
        UserId ownerId,
        ScanPresetId presetId,
        IReadOnlyDictionary<string, string> details,
        DateTimeOffset at)
    {
        return new TelegramNotification(
            id,
            ownerId,
            botId: null,
            presetId,
            TelegramNotificationKind.ScanAlert,
            alertId: null,
            details,
            at,
            TelegramNotificationStatus.Pending,
            attempts: 0,
            at,
            claimId: null,
            claimedUntil: null,
            sentAt: null,
            lastError: null);
    }

    public static TelegramNotification Restore(
        TelegramNotificationId id,
        UserId ownerId,
        BotId? botId,
        ScanPresetId? presetId,
        TelegramNotificationKind kind,
        BotAlertId? alertId,
        IReadOnlyDictionary<string, string> details,
        DateTimeOffset createdAt,
        TelegramNotificationStatus status,
        int attempts,
        DateTimeOffset nextAttemptAt,
        TelegramClaimId? claimId,
        DateTimeOffset? claimedUntil,
        DateTimeOffset? sentAt,
        string? lastError)
    {
        return new TelegramNotification(
            id,
            ownerId,
            botId,
            presetId,
            kind,
            alertId,
            details,
            createdAt,
            status,
            attempts,
            nextAttemptAt,
            claimId,
            claimedUntil,
            sentAt,
            lastError);
    }

    /// <summary>
    ///     Whether a sender may claim it now: pending, its next attempt has come and no live claim holds it.
    /// </summary>
    public bool IsDue(DateTimeOffset now)
    {
        return Status == TelegramNotificationStatus.Pending && NextAttemptAt <= now && (ClaimedUntil is not { } until || until <= now);
    }

    public bool IsTooOld(DateTimeOffset now)
    {
        return now - CreatedAt > MaxAge;
    }

    /// <summary>
    ///     Claims it for a sender; returns whether it was due.
    /// </summary>
    public bool Claim(TelegramClaimId claimId, DateTimeOffset now)
    {
        if (!IsDue(now))
        {
            return false;
        }

        ClaimId = claimId;
        ClaimedUntil = now + ClaimDuration;

        return true;
    }

    /// <summary>
    ///     Telegram accepted the message.
    /// </summary>
    public bool MarkSent(TelegramClaimId claimId, DateTimeOffset now)
    {
        return Finish(claimId, TelegramNotificationStatus.Sent, error: null, now);
    }

    /// <summary>
    ///     Too old to be sent (<see cref="IsTooOld" />).
    /// </summary>
    public bool Skip(TelegramClaimId claimId, DateTimeOffset now)
    {
        return IsTooOld(now) && Finish(claimId, TelegramNotificationStatus.Skipped, error: null, now);
    }

    /// <summary>
    ///     The owner blocked the bot or no longer has a linked chat.
    /// </summary>
    public bool MarkChatUnavailable(TelegramClaimId claimId, DateTimeOffset now)
    {
        return Finish(claimId, TelegramNotificationStatus.ChatUnavailable, error: null, now);
    }

    /// <summary>
    ///     Telegram refused the message for good.
    /// </summary>
    public bool Fail(TelegramClaimId claimId, string error, DateTimeOffset now)
    {
        return Finish(claimId, TelegramNotificationStatus.Failed, error, now);
    }

    /// <summary>
    ///     Telegram could not take the message now: it waits by <see cref="TelegramRetrySchedule" /> and is tried again.
    /// </summary>
    public bool RetryLater(TelegramClaimId claimId, string error, TimeSpan? retryAfter, DateTimeOffset now)
    {
        if (!IsClaimedBy(claimId))
        {
            return false;
        }

        Attempts++;
        NextAttemptAt = TelegramRetrySchedule.NextAttemptAt(Attempts, retryAfter, now);
        LastError = Limit(error);
        ClaimId = null;
        ClaimedUntil = null;

        return true;
    }

    private bool Finish(TelegramClaimId claimId, TelegramNotificationStatus status, string? error, DateTimeOffset now)
    {
        if (!IsClaimedBy(claimId))
        {
            return false;
        }

        Status = status;

        if (status == TelegramNotificationStatus.Sent)
        {
            Attempts++;
            SentAt = now;
        }
        else if (status == TelegramNotificationStatus.Failed)
        {
            Attempts++;
            LastError = Limit(error);
        }

        ClaimId = null;
        ClaimedUntil = null;

        return true;
    }

    /// <summary>
    ///     An outcome counts only from the sender whose claim is the current one, even after the claim's expiry when nobody
    ///     took it over.
    /// </summary>
    private bool IsClaimedBy(TelegramClaimId claimId)
    {
        return Status == TelegramNotificationStatus.Pending && ClaimId == claimId;
    }

    private static string? Limit(string? error)
    {
        return error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error;
    }
}