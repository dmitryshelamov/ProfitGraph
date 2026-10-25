using MediatR;

using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.CreateTelegramLinkCode;

internal sealed class CreateTelegramLinkCodeCommandHandler(ITelegramLinkRepository links, TimeProvider clock)
    : IRequestHandler<CreateTelegramLinkCodeCommand, TelegramLinkCodeInternal>
{
    public async Task<TelegramLinkCodeInternal> Handle(CreateTelegramLinkCodeCommand command, CancellationToken cancellationToken)
    {
        IssuedTelegramLinkCode issued = TelegramLinkCode.Issue(new UserId(command.UserId), clock.GetUtcNow());
        await links.SaveCode(issued.Stored, cancellationToken);

        return new TelegramLinkCodeInternal(issued.Code, issued.Stored.ExpiresAt);
    }
}