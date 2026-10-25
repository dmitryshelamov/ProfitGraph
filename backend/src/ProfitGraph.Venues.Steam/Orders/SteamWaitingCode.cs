namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Why an order accepted only locally has not reached Steam yet (ADR 0022, waiting codes); texts are made at the boundary.
/// </summary>
internal enum SteamWaitingCode
{
    /// <summary>
    ///     Another buy order of the account holds the item's only buy-order slot.
    /// </summary>
    SingleBuyOrderSlot,

    /// <summary>
    ///     Steam waits for the order's mobile confirmation.
    /// </summary>
    AwaitingConfirmation,

    /// <summary>
    ///     The account's requests pause, or the request was not sent; it is tried again.
    /// </summary>
    RateLimited,

    /// <summary>
    ///     The items to sell are still under their trade hold; the earliest unlock time is on their reservations.
    /// </summary>
    SettlementHold
}