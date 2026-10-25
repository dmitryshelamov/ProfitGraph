using MediatR;

using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.LinkTelegramChat;

internal sealed class LinkTelegramChatCommandHandler(ITelegramLinkRepository links, TimeProvider clock) : IRequestHandler<LinkTelegramChatCommand, bool>
{
    public async Task<bool> Handle(LinkTelegramChatCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        TelegramLinkCode? code = await links.TakeCode(TelegramLinkCode.Hash(command.Code), cancellationToken);

        if (code is null || code.IsExpired(now))
        {
            return false;
        }

        await links.Save(TelegramLink.Link(code.UserId, command.ChatId, now), cancellationToken);

        return true;
    }
}