namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     A claimed notification ready to be sent: the chat it goes to and its HTML text.
/// </summary>
public sealed record TelegramOutgoingInternal(Guid NotificationId, long ChatId, string Text);