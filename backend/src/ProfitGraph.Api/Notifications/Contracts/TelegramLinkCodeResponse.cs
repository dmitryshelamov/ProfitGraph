namespace ProfitGraph.Api.Notifications.Contracts;

/// <summary>
///     A one-time code to send to the bot as <c>/start CODE</c>, and a link that does so when the bot is known.
/// </summary>
public sealed record TelegramLinkCodeResponse(string Code, DateTimeOffset ExpiresAt, string? StartLink);