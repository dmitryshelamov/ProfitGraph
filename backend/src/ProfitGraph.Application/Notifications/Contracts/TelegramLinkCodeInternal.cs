namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     A link code in plain text, shown once, and when it expires.
/// </summary>
public sealed record TelegramLinkCodeInternal(string Code, DateTimeOffset ExpiresAt);