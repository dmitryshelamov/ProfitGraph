namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     What a Telegram notification reports (ADR 0042).
/// </summary>
public enum TelegramNotificationKind
{
    /// <summary>
    ///     A bot alert (ADR 0018); its message carries the «Принято» button.
    /// </summary>
    Alert,

    /// <summary>
    ///     The owner started the bot.
    /// </summary>
    Started,

    /// <summary>
    ///     The owner stopped the bot; the runtime is still finishing its work.
    /// </summary>
    StopRequested,

    /// <summary>
    ///     The runtime stopped processing the bot.
    /// </summary>
    Stopped,

    /// <summary>
    ///     The bot runs again after a degradation or failed restarts.
    /// </summary>
    Recovered,

    /// <summary>
    ///     The automatic restarts are spent; the bot waits for its owner.
    /// </summary>
    WaitingForOwner,

    /// <summary>
    ///     The position became flat, with the cycle's realized profit.
    /// </summary>
    PositionClosed,

    /// <summary>
    ///     An order was executed completely, with its totals.
    /// </summary>
    OrderFilled
}