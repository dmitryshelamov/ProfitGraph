namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     Where a Telegram notification stands (ADR 0042): waiting to be sent, or finished one way or another.
/// </summary>
public enum TelegramNotificationStatus
{
    Pending,
    Sent,

    /// <summary>
    ///     Telegram refused the message for good (400).
    /// </summary>
    Failed,

    /// <summary>
    ///     Too old to be sent when its turn came.
    /// </summary>
    Skipped,

    /// <summary>
    ///     The owner blocked the bot or has no linked chat any more.
    /// </summary>
    ChatUnavailable
}