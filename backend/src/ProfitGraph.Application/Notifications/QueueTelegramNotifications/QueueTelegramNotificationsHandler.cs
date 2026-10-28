using System.Globalization;

using MediatR;

using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.QueueTelegramNotifications;

/// <summary>
///     Queues a Telegram notification when the outbox delivers an event its owner should hear about (ADR 0042): an alert, a
///     start, a stop, a recovery, spent restarts, a closed position and a filled order. Only while the owner has a linked chat
///     that is not blocked. The outbox delivers each event once, inside the transaction that stores the notification.
///     Details are invariant strings: names and exact decimals.
/// </summary>
internal sealed class QueueTelegramNotificationsHandler(IBotRepository bots, ITelegramLinkRepository links, ITelegramNotificationRepository notifications)
    : INotificationHandler<BotAlertRaised>,
        INotificationHandler<BotStarted>,
        INotificationHandler<BotStopped>,
        INotificationHandler<BotRuntimeStopped>,
        INotificationHandler<BotRecovered>,
        INotificationHandler<BotPositionClosed>,
        INotificationHandler<BotOrderFilled>
{
    private static readonly IReadOnlyDictionary<string, string> NoDetails = new Dictionary<string, string>();

    public Task Handle(BotAlertRaised notification, CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, string> { ["kind"] = notification.Kind.ToString() };

        if (notification.Reason is { } reason)
        {
            details["reason"] = reason;
        }

        return Queue(notification.OwnerId, notification.BotId, TelegramNotificationKind.Alert, notification.AlertId, details, notification.At, cancellationToken);
    }

    public Task Handle(BotOrderFilled notification, CancellationToken cancellationToken)
    {
        BotOrderFill fill = notification.Fill;
        var details = new Dictionary<string, string>
        {
            ["engineOrderId"] = fill.EngineOrderId.ToString(CultureInfo.InvariantCulture),
            ["side"] = fill.Side.ToString(),
            ["grossBase"] = ExactDecimalText.Format(fill.GrossBase),
            ["grossQuote"] = ExactDecimalText.Format(fill.GrossQuote),
            ["fee"] = ExactDecimalText.Format(fill.Fee),
            ["feeAsset"] = fill.FeeAsset.ToString(),
            ["quantityStep"] = ExactDecimalText.Format(fill.QuantityStep),
            ["priceTick"] = ExactDecimalText.Format(fill.PriceTick),
            ["quotePrecision"] = fill.QuotePrecision.ToString(CultureInfo.InvariantCulture)
        };

        if (fill.RealizedPnl is { } realized)
        {
            details["realizedPnl"] = ExactDecimalText.Format(realized);
        }

        if (fill.Tag is { } tag)
        {
            details["tag"] = tag;
        }

        return Queue(notification.BotId, TelegramNotificationKind.OrderFilled, details, notification.At, cancellationToken);
    }

    public Task Handle(BotPositionClosed notification, CancellationToken cancellationToken)
    {
        return Queue(
            notification.BotId,
            TelegramNotificationKind.PositionClosed,
            new Dictionary<string, string> { ["realizedPnl"] = ExactDecimalText.Format(notification.RealizedPnl) },
            notification.At,
            cancellationToken);
    }

    public Task Handle(BotRecovered notification, CancellationToken cancellationToken)
    {
        return Queue(notification.BotId, TelegramNotificationKind.Recovered, NoDetails, notification.At, cancellationToken);
    }

    public Task Handle(BotRuntimeStopped notification, CancellationToken cancellationToken)
    {
        return Queue(notification.BotId, TelegramNotificationKind.Stopped, NoDetails, notification.At, cancellationToken);
    }

    public Task Handle(BotStarted notification, CancellationToken cancellationToken)
    {
        return Queue(notification.BotId, TelegramNotificationKind.Started, NoDetails, notification.At, cancellationToken);
    }

    public Task Handle(BotStopped notification, CancellationToken cancellationToken)
    {
        return Queue(notification.BotId, TelegramNotificationKind.StopRequested, NoDetails, notification.At, cancellationToken);
    }

    private async Task Queue(
        BotId botId,
        TelegramNotificationKind kind,
        IReadOnlyDictionary<string, string> details,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (await bots.Get(botId, cancellationToken) is { } bot)
        {
            await Queue(bot.OwnerId, botId, kind, alertId: null, details, at, cancellationToken);
        }
    }

    private async Task Queue(
        UserId ownerId,
        BotId botId,
        TelegramNotificationKind kind,
        BotAlertId? alertId,
        IReadOnlyDictionary<string, string> details,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (await links.Get(ownerId, cancellationToken) is { BlockedAt: null })
        {
            await notifications.Add(TelegramNotification.Queue(TelegramNotificationId.New(), ownerId, botId, kind, alertId, details, at), cancellationToken);
        }
    }
}