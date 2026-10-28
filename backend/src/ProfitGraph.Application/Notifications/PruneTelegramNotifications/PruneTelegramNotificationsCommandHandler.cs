using MediatR;

using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.PruneTelegramNotifications;

internal sealed class PruneTelegramNotificationsCommandHandler(ITelegramNotificationRepository notifications, TimeProvider clock)
    : IRequestHandler<PruneTelegramNotificationsCommand, int>
{
    public Task<int> Handle(PruneTelegramNotificationsCommand command, CancellationToken cancellationToken)
    {
        return notifications.DeleteFinishedBefore(clock.GetUtcNow() - TelegramNotification.Retention, cancellationToken);
    }
}