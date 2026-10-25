namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     The items of an item kind a bot may sell (ADR 0022, ownership): only assets attributed to the bot's own buys, as
///     market history and the inventory prove them. Assets another open sell reserved are still included; the journal's
///     reservations keep them apart.
/// </summary>
internal interface ISteamOwnedAssets
{
    Task<IReadOnlyList<SteamOwnedAsset>> Owned(Guid botId, Guid credentialId, SteamInstrument instrument, CancellationToken cancellationToken);
}