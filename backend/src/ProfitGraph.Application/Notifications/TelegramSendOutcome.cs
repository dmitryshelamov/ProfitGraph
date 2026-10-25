namespace ProfitGraph.Application.Notifications;

public enum TelegramSendOutcome
{
    Sent,

    /// <summary>
    ///     The user blocked the bot; nothing more can be sent to the chat.
    /// </summary>
    Blocked
}