using MediatR;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.LinkTelegramChat;

/// <summary>
///     Links the chat to the user of a one-time code. A code of a blocked account links nothing (ADR 0043).
/// </summary>
internal sealed class LinkTelegramChatCommandHandler(ITelegramLinkRepository links, IUserRepository users, TimeProvider clock)
    : IRequestHandler<LinkTelegramChatCommand, bool>
{
    public async Task<bool> Handle(LinkTelegramChatCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        TelegramLinkCode? code = await links.TakeCode(TelegramLinkCode.Hash(command.Code), cancellationToken);

        if (code is null || code.IsExpired(now) || await users.GetById(code.UserId, cancellationToken) is not { IsActive: true })
        {
            return false;
        }

        await links.Save(TelegramLink.Link(code.UserId, command.ChatId, now), cancellationToken);

        return true;
    }
}