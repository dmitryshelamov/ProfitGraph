using MediatR;

using ProfitGraph.Domain.Bots.Alerts;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.AcknowledgeTelegramAlert;

internal sealed class AcknowledgeTelegramAlertCommandHandler(ITelegramLinkRepository links, IBotAlertRepository alerts, TimeProvider clock)
    : IRequestHandler<AcknowledgeTelegramAlertCommand, TelegramAcknowledgement>
{
    public async Task<TelegramAcknowledgement> Handle(AcknowledgeTelegramAlertCommand command, CancellationToken cancellationToken)
    {
        if (command.AlertId == Guid.Empty || await links.GetByChatForUpdate(command.ChatId, cancellationToken) is not { } link ||
            await alerts.GetForUpdate(new BotAlertId(command.AlertId), cancellationToken) is not { } alert || alert.OwnerId != link.UserId)
        {
            return TelegramAcknowledgement.NotFound;
        }

        if (!alert.Acknowledge(clock.GetUtcNow()))
        {
            return TelegramAcknowledgement.AlreadyAcknowledged;
        }

        await alerts.Update(alert, cancellationToken);

        return TelegramAcknowledgement.Acknowledged;
    }
}