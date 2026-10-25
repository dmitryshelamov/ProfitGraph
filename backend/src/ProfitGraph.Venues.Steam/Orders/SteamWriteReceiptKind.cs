namespace ProfitGraph.Venues.Steam.Orders;

internal enum SteamWriteReceiptKind
{
    /// <summary>
    ///     Steam answered success; a place answer carries the new Steam ID.
    /// </summary>
    Accepted,

    /// <summary>
    ///     Steam waits for a mobile confirmation whose creator the receipt carries.
    /// </summary>
    NeedsConfirmation,

    /// <summary>
    ///     Steam refused with a code the owner confirmed as a definite rejection (<see cref="SteamOptions.DefiniteRejectionCodes" />).
    /// </summary>
    Rejected,

    /// <summary>
    ///     The request never left the process; it may be sent again.
    /// </summary>
    NotSent,

    /// <summary>
    ///     The write may or may not have taken effect, including a 429 or a refusal with an unconfirmed code.
    /// </summary>
    Unknown
}