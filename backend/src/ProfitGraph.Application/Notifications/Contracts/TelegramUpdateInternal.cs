namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     An update Telegram reports to the bot: its identifier and, for a text message, the chat and text.
/// </summary>
public sealed record TelegramUpdateInternal(long UpdateId, long? ChatId, string? Text);