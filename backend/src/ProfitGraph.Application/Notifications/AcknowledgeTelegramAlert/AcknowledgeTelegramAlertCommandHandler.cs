using MediatR;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots.Alerts;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.AcknowledgeTelegramAlert;

/// <summary>
///     Acknowledges an alert of the chat's user. A blocked account acknowledges nothing: its alerts are not found (ADR 0043).
/// </summary>
internal sealed class AcknowledgeTelegramAlertCommandHandler(
    ITelegramLinkRepository links,
    IUserRepository users,
    IBotAlertRepository alerts,
    TimeProvider clock)
    : IRequestHandler<AcknowledgeTelegramAlertCommand, TelegramAcknowledgement>
{
    public async Task<TelegramAcknowledgement> Handle(AcknowledgeTelegramAlertCommand command, CancellationToken cancellationToken)
    {
        if (command.AlertId == Guid.Empty || await links.GetByChatForUpdate(command.ChatId, cancellationToken) is not { } link ||
            await users.GetById(link.UserId, cancellationToken) is not { IsActive: true } ||
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