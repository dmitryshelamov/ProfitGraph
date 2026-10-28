namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     A press of a button under one of the bot's messages: the identifier Telegram expects an answer to, whether the presser
///     is a bot, the message's chat, its type and the message, when Telegram still has them, and the button's data.
/// </summary>
public sealed record TelegramCallbackInternal(string Id, bool FromBot, long? ChatId, string? ChatType, long? MessageId, string? Data);