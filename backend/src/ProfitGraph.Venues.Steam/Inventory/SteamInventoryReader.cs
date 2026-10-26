using System.Globalization;

using Microsoft.Extensions.Options;

using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Inventory;

/// <summary>
///     Reads one app's whole inventory of an account through its session at account priority (its first page as a browser
///     navigation, which Steam may send through the market eligibility check and back): every context the app
///     advertises, every page up to <see cref="SteamOptions.InventoryMaximumPages" />. Bought items under a trade hold may
///     lie in another context than the usual one, so no context is assumed.
/// </summary>
internal sealed class SteamInventoryReader(IOptions<SteamOptions> options, TimeProvider clock)
{
    public async Task<IReadOnlyList<SteamInventoryAsset>> Read(SteamAccountSession session, int appId, CancellationToken cancellationToken)
    {
        string steamId = Uri.EscapeDataString(await session.SteamId(cancellationToken));
        byte[] page = Body(await session.ReadPage($"/profiles/{steamId}/inventory/?l=english", SteamRequestClass.Account, cancellationToken));
        var assets = new List<SteamInventoryAsset>();

        foreach (string context in SteamInventoryParser.Contexts(page, appId))
        {
            string? start = null;

            for (var number = 0;; number++)
            {
                if (number >= options.Value.InventoryMaximumPages)
                {
                    throw new SteamInventoryException("The Steam inventory is longer than ProfitGraph reads.");
                }

                var path = string.Create(
                    CultureInfo.InvariantCulture,
                    $"/inventory/{steamId}/{appId}/{context}?l=english&count={options.Value.InventoryPageSize}{(start is null ? "" : "&start_assetid=" + Uri.EscapeDataString(start))}");
                SteamInventoryPage read = SteamInventoryParser.Page(
                    Body(await session.Execute(SteamHttpRequest.Get(SteamEndpoint.Community, path, SteamRequestClass.Account), cancellationToken)),
                    appId,
                    context,
                    clock.GetUtcNow());
                assets.AddRange(read.Assets);

                if (read.NextAssetId is null)
                {
                    break;
                }

                start = read.NextAssetId;
            }
        }

        return assets;
    }

    private static byte[] Body(SteamRequestResult result)
    {
        return result.IsSuccess ? result.Response!.Body : throw new SteamInventoryException($"The Steam inventory could not be read ({result.Outcome}).");
    }
}