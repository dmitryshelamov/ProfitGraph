namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     A new link code in plain text, shown to the user once, and the form that is stored.
/// </summary>
public sealed record IssuedTelegramLinkCode(string Code, TelegramLinkCode Stored);