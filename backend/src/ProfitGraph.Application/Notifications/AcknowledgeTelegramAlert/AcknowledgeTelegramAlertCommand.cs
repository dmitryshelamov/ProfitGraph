using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Notifications.AcknowledgeTelegramAlert;

/// <summary>
///     Acknowledges an alert from the «Принято» button (ADR 0042): the user is the one the chat is linked to, and only that
///     user's alert is acknowledged.
/// </summary>
public sealed record AcknowledgeTelegramAlertCommand(long ChatId, Guid AlertId) : ICommand<TelegramAcknowledgement>;