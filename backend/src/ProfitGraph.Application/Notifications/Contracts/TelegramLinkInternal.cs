namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     Whether the user's Telegram chat receives alerts.
/// </summary>
public sealed record TelegramLinkInternal(bool Linked, DateTimeOffset? LinkedAt, bool Blocked);