using MediatR;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.QueueTelegramNotifications;

/// <summary>
///     Queues a Telegram notification when the outbox delivers an event its owner should hear about (ADR 0042), only while
///     the owner has a linked chat that is not blocked. The outbox delivers each event once, inside the transaction that
///     stores the notification. Details are invariant strings.
/// </summary>
internal sealed class QueueTelegramNotificationsHandler(ITelegramLinkRepository links, ITelegramNotificationRepository notifications)
    : INotificationHandler<BotAlertRaised>
{
    public Task Handle(BotAlertRaised notification, CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, string> { ["kind"] = notification.Kind.ToString() };

        if (notification.Reason is { } reason)
        {
            details["reason"] = reason;
        }

        return Queue(notification.OwnerId, notification.BotId, TelegramNotificationKind.Alert, notification.AlertId, details, notification.At, cancellationToken);
    }

    private async Task Queue(
        UserId ownerId,
        BotId botId,
        TelegramNotificationKind kind,
        BotAlertId? alertId,
        IReadOnlyDictionary<string, string> details,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (await links.Get(ownerId, cancellationToken) is { BlockedAt: null })
        {
            await notifications.Add(TelegramNotification.Queue(TelegramNotificationId.New(), ownerId, botId, kind, alertId, details, at), cancellationToken);
        }
    }
}