namespace ProfitGraph.Application.Notifications;

/// <summary>
///     Telegram's answer to a message: the outcome, how long Telegram asked to wait, and the error as our code with
///     Telegram's description, never the request.
/// </summary>
public sealed record TelegramSendResult(TelegramSendOutcome Outcome, TimeSpan? RetryAfter = null, string? Error = null)
{
    public static readonly TelegramSendResult Sent = new TelegramSendResult(TelegramSendOutcome.Sent);
}