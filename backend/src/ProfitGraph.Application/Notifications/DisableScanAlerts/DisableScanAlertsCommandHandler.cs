using MediatR;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Notifications.DisableScanAlerts;

internal sealed class DisableScanAlertsCommandHandler(ITelegramLinkRepository links, IUserRepository users, IScanAlertWatchRepository watches)
    : IRequestHandler<DisableScanAlertsCommand, bool>
{
    public async Task<bool> Handle(DisableScanAlertsCommand command, CancellationToken cancellationToken)
    {
        if (command.PresetId == Guid.Empty || await links.GetByChatForUpdate(command.ChatId, cancellationToken) is not { } link ||
            await users.GetById(link.UserId, cancellationToken) is not { IsActive: true } ||
            await watches.GetForUpdate(new ScanPresetId(command.PresetId), cancellationToken) is not { } watch || watch.OwnerId != link.UserId)
        {
            return false;
        }

        await watches.Delete(watch.PresetId, cancellationToken);

        return true;
    }
}