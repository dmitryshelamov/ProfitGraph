namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     A message sent to the bot: its chat and the chat's type (<c>private</c> for a person's own chat), whether its sender
///     is a bot, and its text when it has one.
/// </summary>
public sealed record TelegramIncomingMessageInternal(long ChatId, string ChatType, bool FromBot, string? Text);