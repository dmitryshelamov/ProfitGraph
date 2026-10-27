using MediatR;

using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.RecordTelegramDelivery;

internal sealed class RecordTelegramDeliveryCommandHandler(ITelegramNotificationRepository notifications, ITelegramLinkRepository links, TimeProvider clock)
    : IRequestHandler<RecordTelegramDeliveryCommand>
{
    public async Task Handle(RecordTelegramDeliveryCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        if (command.Outcome == TelegramSendOutcome.Blocked && await links.GetByChatForUpdate(command.ChatId, cancellationToken) is { } link && link.Block(now))
        {
            await links.Update(link, cancellationToken);
        }

        if (command.NotificationId == Guid.Empty || command.ClaimId == Guid.Empty ||
            await notifications.GetForUpdate(new TelegramNotificationId(command.NotificationId), cancellationToken) is not { } notification)
        {
            return;
        }

        var claimId = new TelegramClaimId(command.ClaimId);
        bool changed = command.Outcome switch
        {
            TelegramSendOutcome.Sent => notification.MarkSent(claimId, now),
            TelegramSendOutcome.Blocked => notification.MarkChatUnavailable(claimId, now),
            TelegramSendOutcome.Rejected => notification.Fail(claimId, command.Error ?? "rejected", now),
            _ => notification.RetryLater(claimId, command.Error ?? "retry_later", command.RetryAfter, now)
        };

        if (changed)
        {
            await notifications.Update(notification, cancellationToken);
        }
    }
}