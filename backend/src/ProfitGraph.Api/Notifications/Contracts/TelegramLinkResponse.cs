namespace ProfitGraph.Api.Notifications.Contracts;

/// <summary>
///     Whether Telegram alerts run in this deployment and whether the user's chat receives them.
/// </summary>
public sealed record TelegramLinkResponse(bool Enabled, bool Linked, DateTimeOffset? LinkedAt, bool Blocked, string? BotUsername);