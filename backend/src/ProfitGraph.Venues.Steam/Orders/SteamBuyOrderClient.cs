using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Options;

using ProfitGraph.Venues.Steam.MarketData;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Steam's buy-order requests through an account's session (Profit Fox Designer <c>SteamMarketClient</c>): create, with
///     the fields of Steam's own form, cancel, status and the item's lowest listing. Writes queue as writes and are never
///     sent twice; the caller decides what a receipt means for its journal entry.
/// </summary>
internal sealed class SteamBuyOrderClient(IOptions<SteamOptions> options)
{
    /// <summary>
    ///     Places a buy order of <paramref name="quantity" /> items at <paramref name="priceMinorUnits" /> each. A buy that
    ///     Steam confirmed on the phone is finalized by sending it again with the confirmation's ID.
    /// </summary>
    public async Task<SteamWriteReceipt> Create(
        SteamAccountSession session,
        SteamInstrument instrument,
        int quantity,
        long priceMinorUnits,
        string? confirmationId,
        CancellationToken cancellationToken)
    {
        // Steam's buy order form (community market.js CreateBuyOrderDialog) always sends the tax, address and confirmation
        // fields; the address stays empty, as on an account without saved billing.
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["appid"] = instrument.AppId.ToString(CultureInfo.InvariantCulture),
            ["market_hash_name"] = instrument.MarketHashName,
            ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
            ["price_total"] = checked(priceMinorUnits * quantity).ToString(CultureInfo.InvariantCulture),
            ["tradefee_tax"] = "0",
            ["first_name"] = string.Empty,
            ["last_name"] = string.Empty,
            ["billing_address"] = string.Empty,
            ["billing_address_two"] = string.Empty,
            ["billing_country"] = string.Empty,
            ["billing_city"] = string.Empty,
            ["billing_state"] = string.Empty,
            ["billing_postal_code"] = string.Empty,
            ["save_my_address"] = "0",
            ["confirmation"] = confirmationId ?? "0"
        };
        SteamRequestResult result = await session.Execute(SteamHttpRequest.Write(SteamEndpoint.Community, "/market/createbuyorder/", form), cancellationToken);

        return SteamWriteReplyParser.Read(result, "buy_orderid", options.Value.DefiniteRejectionCodes);
    }

    public async Task<SteamWriteReceipt> Cancel(SteamAccountSession session, string buyOrderId, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal) { ["buy_orderid"] = buyOrderId };
        SteamRequestResult result = await session.Execute(SteamHttpRequest.Write(SteamEndpoint.Community, "/market/cancelbuyorder/", form), cancellationToken);

        return SteamWriteReplyParser.Read(result, "buy_orderid", options.Value.DefiniteRejectionCodes);
    }

    public async Task<SteamBuyOrderStatus> Status(SteamAccountSession session, string buyOrderId, CancellationToken cancellationToken)
    {
        SteamRequestResult result = await session.Execute(
            SteamHttpRequest.Get(SteamEndpoint.Community, "/market/getbuyorderstatus/?buy_orderid=" + Uri.EscapeDataString(buyOrderId)),
            cancellationToken);

        return result.IsSuccess && SteamBuyOrderStatusParser.Read(result.Response!.Body) is { } status
            ? status
            : throw new SteamOrderUnresolvedException($"Steam's buy order status is unavailable ({result.Outcome}).");
    }

    /// <summary>
    ///     The item's lowest listing in whole minor units of the item's currency, or null when nobody sells it
    ///     (<c>/market/priceoverview/</c>, which Profit Fox Designer read for its market buys).
    /// </summary>
    public async Task<long?> LowestListing(SteamAccountSession session, SteamInstrument instrument, CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"/market/priceoverview/?appid={instrument.AppId}&market_hash_name={Uri.EscapeDataString(instrument.MarketHashName)}&currency={instrument.Currency.SteamId}&l=english");
        SteamRequestResult result = await session.Execute(SteamHttpRequest.Get(SteamEndpoint.Community, path, SteamRequestClass.Quote), cancellationToken);

        if (!result.IsSuccess)
        {
            throw new SteamOrderUnresolvedException($"Steam's lowest listing is unavailable ({result.Outcome}).");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Response!.Body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || SteamWriteReplyParser.Integer(root, "success") != 1)
            {
                throw Unreadable();
            }

            if (!root.TryGetProperty("lowest_price", out JsonElement lowest) || lowest.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return lowest.ValueKind == JsonValueKind.String && SteamPriceText.MinorUnits(lowest.GetString()!, instrument.Currency) is { } units
                ? units
                : throw Unreadable();
        }
        catch (JsonException)
        {
            throw Unreadable();
        }
    }

    private static SteamOrderUnresolvedException Unreadable()
    {
        return new SteamOrderUnresolvedException("Steam's lowest listing is unreadable.");
    }
}