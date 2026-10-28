namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     A claimed notification ready to be sent: the chat it goes to, its HTML text and the buttons under it.
/// </summary>
public sealed record TelegramOutgoingInternal(Guid NotificationId, long ChatId, string Text, IReadOnlyList<TelegramButtonInternal> Buttons);