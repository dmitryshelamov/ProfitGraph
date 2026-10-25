using MediatR;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.RecordBotAlerts;

/// <summary>
///     Records an alert for the bot's owner when the outbox delivers a degradation, a kill, a risk rejection or the first
///     report of a streak in which the venue could not answer (Profit Fox Designer ADR 0056). The outbox delivers each
///     event once, inside the transaction that stores the alert.
/// </summary>
internal sealed class RecordBotAlertsHandler(IBotRepository bots, IBotAlertRepository alerts)
    : INotificationHandler<BotDegraded>,
        INotificationHandler<BotKilled>,
        INotificationHandler<BotExchangeUnavailable>,
        INotificationHandler<OrderIntentRejected>
{
    public Task Handle(BotDegraded notification, CancellationToken cancellationToken)
    {
        return Raise(notification.BotId, BotAlertKind.Degraded, notification.Reason.ToString(), notification.At, cancellationToken);
    }

    public Task Handle(BotExchangeUnavailable notification, CancellationToken cancellationToken)
    {
        return Raise(notification.BotId, BotAlertKind.ExchangeUnavailable, reason: null, notification.At, cancellationToken);
    }

    public Task Handle(BotKilled notification, CancellationToken cancellationToken)
    {
        return Raise(notification.BotId, BotAlertKind.Killed, notification.Reason.ToString(), notification.At, cancellationToken);
    }

    public Task Handle(OrderIntentRejected notification, CancellationToken cancellationToken)
    {
        return notification.Reason == RejectionReason.RiskLimit
            ? Raise(notification.BotId, BotAlertKind.RiskRejection, notification.Reason.ToString(), notification.At, cancellationToken)
            : Task.CompletedTask;
    }

    private async Task Raise(BotId botId, BotAlertKind kind, string? reason, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (await bots.Get(botId, cancellationToken) is { } bot)
        {
            await alerts.Add(BotAlert.Raise(BotAlertId.New(), botId, bot.OwnerId, kind, reason, at), cancellationToken);
        }
    }
}