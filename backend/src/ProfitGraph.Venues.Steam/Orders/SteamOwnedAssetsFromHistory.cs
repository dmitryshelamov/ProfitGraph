using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.Inventory;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     A bot's items from its attributed receipts (ADR 0022, ownership): what its buys bought, under the new owner's asset
///     IDs, and what came back from its cancelled listings, under the returned IDs, less what its sells sold. Only items still
///     in the inventory count, with the inventory's unlock time; an item whose unlock time is not known and that Steam does
///     not let be listed is left out. Nothing is inferred from an item's absence.
/// </summary>
internal sealed class SteamOwnedAssetsFromHistory(
    ISteamMarketHistoryStore receipts,
    ISteamMarketAttributions attributions,
    SteamInventoryReader inventory,
    SteamAccountSessions sessions,
    TimeProvider clock) : ISteamOwnedAssets
{
    public async Task<IReadOnlyList<SteamOwnedAsset>> Owned(SteamBotAccount bot, CancellationToken cancellationToken)
    {
        Dictionary<string, SteamMarketReceipt> all = (await receipts.From(bot.CredentialId, position: 0, cancellationToken)).ToDictionary(
            stored => stored.Receipt.Id,
            stored => stored.Receipt,
            StringComparer.Ordinal);
        SteamMarketReceipt[] mine =
        [
            .. (await attributions.Of(bot.CredentialId, cancellationToken))
            .Where(attribution => attribution.BotId == bot.BotId)
            .Select(attribution => all.GetValueOrDefault(attribution.ReceiptId))
            .OfType<SteamMarketReceipt>()
            .Where(receipt => receipt.AppId == bot.Instrument.AppId && receipt.MarketHashName == bot.Instrument.MarketHashName)
        ];
        HashSet<(string Context, string Asset)> sold = [.. mine.Where(receipt => receipt.Kind == SteamMarketReceiptKind.Sold).Select(Key)];
        HashSet<(string Context, string Asset)> held =
        [
            .. mine.Where(receipt => receipt.Kind is SteamMarketReceiptKind.Bought or SteamMarketReceiptKind.Cancelled).Select(Key).Where(key => !sold.Contains(key))
        ];

        if (held.Count == 0)
        {
            return [];
        }

        IReadOnlyList<SteamInventoryAsset> items;

        try
        {
            items = await inventory.Read(sessions.For(bot.OwnerId, bot.CredentialId), bot.Instrument.AppId, cancellationToken);
        }
        catch (SteamInventoryException exception)
        {
            throw new SteamOrderUnresolvedException(exception.Message);
        }

        DateTimeOffset now = clock.GetUtcNow();

        return
        [
            .. items
                .Where(item => item.MarketHashName == bot.Instrument.MarketHashName && held.Contains((item.ContextId, item.AssetId)) &&
                    (item.UnlockAt is not null || item.Marketable))
                .Select(item => new SteamOwnedAsset(item.AppId, item.ContextId, item.AssetId, item.UnlockAt ?? now))
        ];
    }

    private static (string Context, string Asset) Key(SteamMarketReceipt receipt)
    {
        return (receipt.ContextId, receipt.AssetId);
    }
}