namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     A button under a message; pressing it sends <paramref name="CallbackData" /> back to the bot (at most 64 bytes).
/// </summary>
public sealed record TelegramButtonInternal(string Text, string CallbackData);