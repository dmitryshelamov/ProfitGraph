using MediatR;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.UnlinkTelegram;

internal sealed class UnlinkTelegramCommandHandler(ITelegramLinkRepository links) : IRequestHandler<UnlinkTelegramCommand, bool>
{
    public Task<bool> Handle(UnlinkTelegramCommand command, CancellationToken cancellationToken)
    {
        return links.Delete(new UserId(command.UserId), cancellationToken);
    }
}