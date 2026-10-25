using MediatR;

using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.GetTelegramLink;

internal sealed class GetTelegramLinkQueryHandler(ITelegramLinkRepository links) : IRequestHandler<GetTelegramLinkQuery, TelegramLinkInternal>
{
    public async Task<TelegramLinkInternal> Handle(GetTelegramLinkQuery query, CancellationToken cancellationToken)
    {
        TelegramLink? link = await links.Get(new UserId(query.UserId), cancellationToken);

        return new TelegramLinkInternal(link is not null, link?.LinkedAt, link?.BlockedAt is not null);
    }
}