namespace ProfitGraph.Application.Notifications;

/// <summary>
///     How Telegram answered a message (ADR 0042).
/// </summary>
public enum TelegramSendOutcome
{
    Sent,

    /// <summary>
    ///     The user blocked the bot (403); nothing more can be sent to the chat.
    /// </summary>
    Blocked,

    /// <summary>
    ///     Telegram could not take the message now (429, 5xx, a wrong token); it is tried again.
    /// </summary>
    RetryLater,

    /// <summary>
    ///     Telegram refused the message for good (400).
    /// </summary>
    Rejected
}