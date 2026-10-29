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
    ///     The position became flat, with the cycle's realized profit.
    /// </summary>
    PositionClosed,

    /// <summary>
    ///     An order was executed completely, with its totals.
    /// </summary>
    OrderFilled,

    /// <summary>
    ///     Pairs entered a scanner preset's result (ADR 0059); its message carries the «Отключить» button.
    /// </summary>
    ScanAlert
}