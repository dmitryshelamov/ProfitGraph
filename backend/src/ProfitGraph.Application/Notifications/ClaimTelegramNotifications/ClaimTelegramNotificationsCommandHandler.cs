using MediatR;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.ClaimTelegramNotifications;

/// <summary>
///     Claims due notifications for sending. One whose chat was unlinked or blocked, or whose account was blocked after it was
///     queued (ADR 0043), is <c>ChatUnavailable</c> without a request.
/// </summary>
internal sealed class ClaimTelegramNotificationsCommandHandler(
    ITelegramNotificationRepository notifications,
    ITelegramLinkRepository links,
    IUserRepository users,
    IBotRepository bots,
    VenueRegistry venues,
    TimeProvider clock) : IRequestHandler<ClaimTelegramNotificationsCommand, TelegramClaimInternal>
{
    public async Task<TelegramClaimInternal> Handle(ClaimTelegramNotificationsCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var claimId = new TelegramClaimId(command.ClaimId);
        List<TelegramOutgoingInternal> outgoing = [];
        var claimed = 0;

        foreach (TelegramNotification notification in await notifications.LockDue(now, command.Limit, cancellationToken))
        {
            if (!notification.Claim(claimId, now))
            {
                continue;
            }

            claimed++;

            if (!notification.Skip(claimId, now))
            {
                if (await links.Get(notification.OwnerId, cancellationToken) is { BlockedAt: null } link &&
                    await users.GetById(notification.OwnerId, cancellationToken) is { IsActive: true })
                {
                    Bot? bot = await bots.Get(notification.BotId, cancellationToken);
                    outgoing.Add(
                        new TelegramOutgoingInternal(
                            notification.Id.Value,
                            link.ChatId,
                            TelegramMessages.Notification(notification, bot, bot is null ? null : venues.Find(bot.Series.Venue.Value)),
                            TelegramMessages.Buttons(notification)));
                }
                else
                {
                    notification.MarkChatUnavailable(claimId, now);
                }
            }

            await notifications.Update(notification, cancellationToken);
        }

        return new TelegramClaimInternal(claimed, outgoing);
    }
}