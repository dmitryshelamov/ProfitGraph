namespace ProfitGraph.Venues.Steam.Confirmations;

/// <summary>
///     A pending mobile confirmation: Steam's ID and nonce to answer it, its type and the ID of what created it.
/// </summary>
internal sealed record SteamConfirmation(string Id, string Nonce, int Type, string CreatorId)
{
    /// <summary>
    ///     A buy order (Profit Fox Designer confirmed buys with type 12).
    /// </summary>
    public const int BuyOrderType = 12;

    /// <summary>
    ///     A market listing.
    /// </summary>
    public const int ListingType = 3;
}