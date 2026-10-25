namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Where one reserved asset of a sell stands (ADR 0022). Every state but <see cref="Released" /> and <see cref="Sold" />
///     keeps the asset reserved, so no other sell of the account lists it.
/// </summary>
internal enum SteamReservationState
{
    /// <summary>
    ///     Reserved and not listed: under its hold, or waiting its turn.
    /// </summary>
    Reserved,

    /// <summary>
    ///     The listing request may have left the process.
    /// </summary>
    Listing,

    /// <summary>
    ///     Steam created the listing and waits for its mobile confirmation; its ID is the confirmation's creator.
    /// </summary>
    AwaitingConfirmation,

    /// <summary>
    ///     The approval of the listing's confirmation may have left the process.
    /// </summary>
    Approving,

    Listed,

    /// <summary>
    ///     The removal of the listing may have left the process.
    /// </summary>
    Delisting,

    /// <summary>
    ///     A write may have reached Steam without a clear answer; reconciliation resolves it.
    /// </summary>
    Unknown,

    Released,

    Sold
}