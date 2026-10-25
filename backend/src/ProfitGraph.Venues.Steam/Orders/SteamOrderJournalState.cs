namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Where a Steam order of a bot stands in the module's journal (ADR 0022). Every state but <see cref="Waiting" /> and
///     <see cref="Done" /> holds the item's single buy-order slot of the account, because Steam may have a buy order for it.
/// </summary>
internal enum SteamOrderJournalState
{
    /// <summary>
    ///     Known only to ProfitGraph: nothing was sent, so the order may still be placed or cancelled locally.
    /// </summary>
    Waiting,

    /// <summary>
    ///     The place request may have left the process; until its answer is recorded, nobody knows whether Steam has the
    ///     order.
    /// </summary>
    Placing,

    /// <summary>
    ///     Steam asked for a mobile confirmation of the order; its creator is recorded.
    /// </summary>
    AwaitingConfirmation,

    /// <summary>
    ///     Steam has the order under its own ID.
    /// </summary>
    Active,

    /// <summary>
    ///     A cancel was accepted, and the order's final counters are not read yet.
    /// </summary>
    Cancelling,

    /// <summary>
    ///     A write may have reached Steam without a clear answer; only reconciliation resolves it, and nothing is sent again
    ///     for it before.
    /// </summary>
    Unknown,

    /// <summary>
    ///     Final: rejected, cancelled or filled.
    /// </summary>
    Done
}