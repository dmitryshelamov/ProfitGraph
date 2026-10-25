using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     The items a test says the bot owns.
/// </summary>
internal sealed class FakeSteamOwnedAssets : ISteamOwnedAssets
{
    public List<SteamOwnedAsset> Assets { get; } = new List<SteamOwnedAsset>();

    public Task<IReadOnlyList<SteamOwnedAsset>> Owned(Guid botId, Guid credentialId, SteamInstrument instrument, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SteamOwnedAsset>>(Assets);
    }
}