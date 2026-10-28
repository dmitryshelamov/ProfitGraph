namespace ProfitGraph.Application.Notifications;

/// <summary>
///     What a press of «Принято» did to its alert (ADR 0042).
/// </summary>
public enum TelegramAcknowledgement
{
    Acknowledged,
    AlreadyAcknowledged,

    /// <summary>
    ///     No such alert of the chat's user, or the chat is linked to nobody.
    /// </summary>
    NotFound
}