namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     What a Telegram notification reports (ADR 0042).
/// </summary>
public enum TelegramNotificationKind
{
    /// <summary>
    ///     A bot alert (ADR 0018); its message carries the «Принято» button.
    /// </summary>
    Alert
}