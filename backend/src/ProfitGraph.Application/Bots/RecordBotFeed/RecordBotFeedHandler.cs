using System.Globalization;

using MediatR;

using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.RecordBotFeed;

/// <summary>
///     Records the bot's event feed from the Domain events the outbox delivers (ADR 0021), once each, in the delivery
///     transaction. Details are invariant strings: reasons and states by name, amounts as exact decimals.
/// </summary>
internal sealed class RecordBotFeedHandler(IBotFeedRepository feed)
    : INotificationHandler<BotStarted>,
        INotificationHandler<BotStopped>,
        INotificationHandler<BotKilled>,
        INotificationHandler<BotDegraded>,
        INotificationHandler<BotRecovered>,
        INotificationHandler<BotRuntimeStopped>,
        INotificationHandler<BotRunRestarted>,
        INotificationHandler<BotGraphVersionApplied>,
        INotificationHandler<BotGraphChangeRejected>,
        INotificationHandler<BotFeedStalled>,
        INotificationHandler<BotFeedRecovered>,
        INotificationHandler<BotExchangeUnavailable>,
        INotificationHandler<BotSubmitHeld>,
        INotificationHandler<OrderIntentPlaceStarted>,
        INotificationHandler<OrderIntentRejected>,
        INotificationHandler<OrderIntentFailed>,
        INotificationHandler<OrderIntentAbsenceConfirmed>,
        INotificationHandler<OrderIntentAbsenceProven>,
        INotificationHandler<BotFillRecorded>,
        INotificationHandler<BotPositionOpened>,
        INotificationHandler<BotPositionClosed>,
        INotificationHandler<BotParametersChanged>,
        INotificationHandler<BotParametersFitted>,
        INotificationHandler<BotRiskLimitsChanged>,
        INotificationHandler<BotForeignOrdersPolicyChanged>,
        INotificationHandler<BotFeeRatesChanged>,
        INotificationHandler<BotFeeCorrected>,
        INotificationHandler<BotOrderWaitingStarted>,
        INotificationHandler<BotOrderWaitingEnded>,
        INotificationHandler<BotHistoryRestated>
{
    public Task Handle(BotDegraded notification, CancellationToken cancellationToken)
    {
        return notification.Detail is { } detail
            ? Record(
                notification.BotId,
                BotFeedKind.Degraded,
                notification.At,
                cancellationToken,
                ("reason", notification.Reason.ToString()),
                ("detail", detail))
            : Record(notification.BotId, BotFeedKind.Degraded, notification.At, cancellationToken, ("reason", notification.Reason.ToString()));
    }

    public Task Handle(BotExchangeUnavailable notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.ExchangeUnavailable, notification.At, cancellationToken);
    }

    public Task Handle(BotFeeCorrected notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.FeeCorrected,
            notification.At,
            cancellationToken,
            ("engineOrderId", Number(notification.EngineOrderId)),
            ("previousFee", Number(notification.PreviousFee)),
            ("correctedFee", Number(notification.CorrectedFee)));
    }

    public Task Handle(BotFeeRatesChanged notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.SettingsChanged, notification.At, cancellationToken, ("setting", "feeRates"));
    }

    public Task Handle(BotFeedRecovered notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.FeedRecovered, notification.At, cancellationToken);
    }

    public Task Handle(BotFeedStalled notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.FeedStalled, notification.At, cancellationToken);
    }

    public Task Handle(BotFillRecorded notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.Fill,
            notification.At,
            cancellationToken,
            ("fillId", notification.FillId.Value.ToString("D")),
            ("engineOrderId", Number(notification.EngineOrderId)),
            ("side", notification.Side.ToString()),
            ("grossBase", Number(notification.GrossBase)),
            ("grossQuote", Number(notification.GrossQuote)));
    }

    public Task Handle(BotForeignOrdersPolicyChanged notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.SettingsChanged,
            notification.At,
            cancellationToken,
            ("setting", "foreignOrders"),
            ("allowForeignOrders", notification.AllowForeignOrders ? "true" : "false"));
    }

    public Task Handle(BotGraphChangeRejected notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.GraphChangeRejected,
            notification.At,
            cancellationToken,
            ("strategyVersionId", notification.StrategyVersionId.Value.ToString("D")));
    }

    public Task Handle(BotGraphVersionApplied notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.GraphVersionApplied,
            notification.At,
            cancellationToken,
            ("strategyVersionId", notification.StrategyVersionId.Value.ToString("D")));
    }

    public Task Handle(BotHistoryRestated notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.HistoryRestated,
            notification.At,
            cancellationToken,
            ("openTime", notification.OpenTime.ToString("O", CultureInfo.InvariantCulture)),
            ("previousClose", Number(notification.PreviousClose)),
            ("restatedClose", Number(notification.RestatedClose)));
    }

    public Task Handle(BotKilled notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.Killed, notification.At, cancellationToken, ("reason", notification.Reason.ToString()));
    }

    public Task Handle(BotOrderWaitingEnded notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.OrderWaitingEnded,
            notification.At,
            cancellationToken,
            ("engineOrderId", Number(notification.EngineOrderId)),
            ("code", notification.Code.ToString()));
    }

    public Task Handle(BotOrderWaitingStarted notification, CancellationToken cancellationToken)
    {
        var details = new List<(string, string)> { ("engineOrderId", Number(notification.EngineOrderId)), ("code", notification.Code.ToString()) };

        if (notification.Until is { } until)
        {
            details.Add(("until", until.ToString("O", CultureInfo.InvariantCulture)));
        }

        if (notification.Low is { } low)
        {
            details.Add(("low", Number(low)));
        }

        if (notification.High is { } high)
        {
            details.Add(("high", Number(high)));
        }

        return Record(notification.BotId, BotFeedKind.OrderWaiting, notification.At, cancellationToken, [.. details]);
    }

    public Task Handle(BotParametersChanged notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.SettingsChanged, notification.At, cancellationToken, ("setting", "parameters"));
    }

    /// <summary>
    ///     The values followed a new strategy version (ADR 0039): <c>added</c> lists <c>name=default</c> pairs, <c>removed</c> names.
    /// </summary>
    public Task Handle(BotParametersFitted notification, CancellationToken cancellationToken)
    {
        var details = new List<(string Name, string Value)> { ("setting", "parameters") };

        if (notification.Added.Count > 0)
        {
            details.Add(
                ("added", string.Join(", ", notification.Added.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={Number(pair.Value)}"))));
        }

        if (notification.Removed.Count > 0)
        {
            details.Add(("removed", string.Join(", ", notification.Removed.Order(StringComparer.Ordinal))));
        }

        return Record(notification.BotId, BotFeedKind.SettingsChanged, notification.At, cancellationToken, [.. details]);
    }

    public Task Handle(BotPositionClosed notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.PositionClosed,
            notification.At,
            cancellationToken,
            ("cycleId", notification.CycleId.Value.ToString("D")),
            ("realizedPnl", Number(notification.RealizedPnl)));
    }

    public Task Handle(BotPositionOpened notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.PositionOpened, notification.At, cancellationToken, ("cycleId", notification.CycleId.Value.ToString("D")));
    }

    public Task Handle(BotRecovered notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.Recovered, notification.At, cancellationToken);
    }

    public Task Handle(BotRiskLimitsChanged notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.SettingsChanged, notification.At, cancellationToken, ("setting", "riskLimits"));
    }

    public Task Handle(BotRunRestarted notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.RunRestarted, notification.At, cancellationToken);
    }

    public Task Handle(BotRuntimeStopped notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.RuntimeStopped, notification.At, cancellationToken);
    }

    public Task Handle(BotStarted notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.Started,
            notification.At,
            cancellationToken,
            ("strategyVersionId", notification.StrategyVersionId.Value.ToString("D")));
    }

    public Task Handle(BotStopped notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.Stopped, notification.At, cancellationToken);
    }

    public Task Handle(BotSubmitHeld notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.SubmitHeld, notification.At, cancellationToken);
    }

    public Task Handle(OrderIntentAbsenceConfirmed notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.OrderAbsenceConfirmed, notification.At, cancellationToken, ("engineOrderId", Number(notification.EngineOrderId)));
    }

    public Task Handle(OrderIntentAbsenceProven notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.OrderAbsenceProven,
            notification.At,
            cancellationToken,
            ("engineOrderId", Number(notification.EngineOrderId)),
            ("checks", string.Join(separator: ',', notification.Checks)));
    }

    public Task Handle(OrderIntentFailed notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.OrderFailed, notification.At, cancellationToken, ("engineOrderId", Number(notification.EngineOrderId)));
    }

    public Task Handle(OrderIntentPlaceStarted notification, CancellationToken cancellationToken)
    {
        return Record(notification.BotId, BotFeedKind.OrderPlaced, notification.At, cancellationToken, ("engineOrderId", Number(notification.EngineOrderId)));
    }

    public Task Handle(OrderIntentRejected notification, CancellationToken cancellationToken)
    {
        return Record(
            notification.BotId,
            BotFeedKind.OrderRejected,
            notification.At,
            cancellationToken,
            ("engineOrderId", Number(notification.EngineOrderId)),
            ("reason", notification.Reason.ToString()));
    }

    private Task Record(BotId botId, BotFeedKind kind, DateTimeOffset at, CancellationToken cancellationToken, params (string Name, string Value)[] details)
    {
        return feed.Add(
            BotFeedEntry.Record(BotFeedEntryId.New(), botId, kind, details.ToDictionary(detail => detail.Name, detail => detail.Value), at),
            cancellationToken);
    }

    private static string Number(decimal value)
    {
        return ExactDecimalText.Format(value);
    }
}