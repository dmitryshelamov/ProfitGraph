namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     One asset a sell lists: the item's place in the inventory, when it may be sold, what the seller receives for it in
///     whole minor units, fixed when the sell was planned, and its Steam listing once it has one.
/// </summary>
internal sealed record SteamAssetReservation(
    string ClientOrderId,
    Guid CredentialId,
    int AppId,
    string ContextId,
    string AssetId,
    DateTimeOffset UnlockAt,
    long SellerMinorUnits,
    SteamReservationState State,
    string? ListingId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsOpen => State is not (SteamReservationState.Released or SteamReservationState.Sold);
}