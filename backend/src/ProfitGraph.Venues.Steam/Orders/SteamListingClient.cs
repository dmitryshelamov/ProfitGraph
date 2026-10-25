using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Options;

using ProfitGraph.Venues.Steam.MarketData;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Steam's listing requests through an account's session (Profit Fox Designer <c>SteamMarketClient.Sell</c>, <c>Cancel</c>
///     and its listings read): list one asset at the amount the seller receives, remove a listing, and read every listing of
///     the account. Writes queue as writes and are never sent twice.
/// </summary>
internal sealed class SteamListingClient(IOptions<SteamOptions> options)
{
    /// <summary>
    ///     Lists one asset; <paramref name="sellerMinorUnits" /> is what the seller receives, in whole minor units.
    /// </summary>
    public async Task<SteamWriteReceipt> Sell(SteamAccountSession session, SteamAssetReservation asset, long sellerMinorUnits, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["appid"] = asset.AppId.ToString(CultureInfo.InvariantCulture),
            ["contextid"] = asset.ContextId,
            ["assetid"] = asset.AssetId,
            ["amount"] = "1",
            ["price"] = sellerMinorUnits.ToString(CultureInfo.InvariantCulture)
        };
        SteamRequestResult result = await session.Execute(SteamHttpRequest.Write(SteamEndpoint.Community, "/market/sellitem/", form), cancellationToken);

        return SteamWriteReplyParser.Read(result, "listingid", options.Value.DefiniteRejectionCodes);
    }

    public async Task<SteamWriteReceipt> Remove(SteamAccountSession session, string listingId, CancellationToken cancellationToken)
    {
        SteamRequestResult result = await session.Execute(
            SteamHttpRequest.Write(SteamEndpoint.Community, "/market/removelisting/" + Uri.EscapeDataString(listingId), new Dictionary<string, string>()),
            cancellationToken);

        return SteamWriteReplyParser.Read(result, "listingid", options.Value.DefiniteRejectionCodes);
    }

    /// <summary>
    ///     The item's highest buy order in whole minor units of its currency, or null when nobody buys it; a book in another
    ///     currency is refused.
    /// </summary>
    public async Task<long?> HighestBuyOrder(SteamAccountSession session, SteamInstrument instrument, CancellationToken cancellationToken)
    {
        string query = "[" + instrument.AppId.ToString(CultureInfo.InvariantCulture) + "," + JsonSerializer.Serialize(instrument.MarketHashName) + "]";
        SteamRequestResult result = await session.Execute(
            SteamHttpRequest.Get(SteamEndpoint.Community, "/market/orderbook?q=Load&qp=" + Uri.EscapeDataString(query), SteamRequestClass.Quote),
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new SteamOrderUnresolvedException($"Steam's order book is unavailable ({result.Outcome}).");
        }

        try
        {
            return SteamOrderBookParser.HighestBuy(result.Response!.Body, instrument.Currency) is { } highest
                ? (long)(highest / instrument.Currency.MinorUnit)
                : null;
        }
        catch (SteamMarketDataException exception)
        {
            throw new SteamOrderUnresolvedException(exception.Message);
        }
    }

    /// <summary>
    ///     Every listing of the account, or null when the pages do not add up to Steam's total.
    /// </summary>
    public async Task<IReadOnlyList<SteamListing>?> Listings(SteamAccountSession session, CancellationToken cancellationToken)
    {
        var listings = new List<SteamListing>();
        var start = 0;

        for (var page = 0; page < options.Value.ListingPagesMaximum; page++)
        {
            SteamRequestResult result = await session.Execute(
                SteamHttpRequest.Get(
                    SteamEndpoint.Community,
                    string.Create(CultureInfo.InvariantCulture, $"/market/mylistings/?start={start}&count=100&l=english")),
                cancellationToken);

            if (!result.IsSuccess || SteamListingsParser.Page(result.Response!.Body) is not { } read)
            {
                return null;
            }

            listings.AddRange(read.Listings);

            if (listings.DistinctBy(listing => listing.Id).Count() != listings.Count || listings.Count > read.Total)
            {
                return null;
            }

            if (listings.Count == read.Total)
            {
                return listings;
            }

            if (read.Listings.Count == 0)
            {
                return null;
            }

            start += read.PageSize;
        }

        return null;
    }
}