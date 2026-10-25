using System.Globalization;
using System.Text.Json;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.History;

/// <summary>
///     Normalizes one <c>/market/myhistory/render/?norender=1</c> page into receipts (Profit Fox Designer ADR 0087 and its
///     <c>SteamMarketHistoryParser</c>). Every referenced listing, purchase and asset must resolve inside the page; a
///     purchase must be settled and in the wallet currency (Steam writes 2000 plus the currency's number). A sale names only
///     what the seller received, so the buyer's price follows from the wallet's fee schedule. Anything else leaves the
///     page unreadable, and an unknown event type is never skipped. The reason names only what failed, never identifiers
///     or values.
/// </summary>
internal static class SteamMarketHistoryParser
{
    private const int PurchaseCurrencyOffset = 2000;

    private static readonly JsonElement EmptyMap = JsonDocument.Parse("{}").RootElement.Clone();

    public static SteamMarketHistoryPage? Page(byte[] body, SteamCurrency currency, SellerFeeSchedule fees, out string reason)
    {
        reason = "Shape";

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !Success(root) || !root.TryGetProperty("events", out JsonElement rows) ||
                rows.ValueKind != JsonValueKind.Array || Whole(root, "total_count") is not { } total || Whole(root, "start") is not { } start)
            {
                return null;
            }

            JsonElement listings = Map(root, "listings");
            JsonElement purchases = Map(root, "purchases");
            JsonElement assets = Map(root, "assets");
            var receipts = new List<SteamMarketReceipt>();

            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (Receipt(row, listings, purchases, assets, currency, fees, out reason) is not { } receipt)
                {
                    return null;
                }

                receipts.Add(receipt);
            }

            if (receipts.DistinctBy(receipt => receipt.Id).Count() != receipts.Count || start + receipts.Count > total)
            {
                reason = "Duplicate";

                return null;
            }

            reason = string.Empty;

            return new SteamMarketHistoryPage(total, start, receipts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SteamMarketReceipt? Receipt(
        JsonElement row,
        JsonElement listings,
        JsonElement purchases,
        JsonElement assets,
        SteamCurrency currency,
        SellerFeeSchedule fees,
        out string reason)
    {
        string? listingId = Id(Text(row, "listingid"));
        string? purchaseId = Id(Text(row, "purchaseid"));
        long? type = Whole(row, "event_type");
        long? seconds = Whole(row, "time_event");
        string fraction = Text(row, "time_event_fraction") ?? "0";
        string kind = "@" + (type?.ToString(CultureInfo.InvariantCulture) ?? "?");

        if (listingId is null || seconds is null or > 253402300799 || fraction.Length > 32 || !fraction.All(char.IsAsciiDigit))
        {
            reason = "Reference:Event" + kind;

            return null;
        }

        if (!listings.TryGetProperty(listingId, out JsonElement listing) || listing.ValueKind != JsonValueKind.Object ||
            !listing.TryGetProperty("asset", out JsonElement listed) || listed.ValueKind != JsonValueKind.Object)
        {
            reason = "Reference:Listing" + kind;

            return null;
        }

        string id = string.Join(
            separator: ':',
            type?.ToString(CultureInfo.InvariantCulture),
            listingId,
            purchaseId ?? "0",
            seconds.Value.ToString(CultureInfo.InvariantCulture),
            fraction);
        DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(seconds.Value);

        switch (type)
        {
            case 1 or 2:
            {
                if (Asset(listed, assets, fallback: null, out SteamItemReference item, out JsonElement description) is { } failure)
                {
                    reason = failure + kind;

                    return null;
                }

                reason = string.Empty;

                if (type == 1)
                {
                    return item.Receipt(id, SteamMarketReceiptKind.Listed, listingId, item.ContextId, item.AssetId, buyer: 0, seller: 0, at);
                }

                // A cancelled listing returns the item, possibly under a new asset ID recorded on its description.
                string returned = Id(Text(description, "new_id")) ?? Id(Text(listed, "new_id")) ?? item.AssetId;
                string returnedContext = Id(Text(description, "new_contextid")) ?? item.ContextId;

                return item.Receipt(id, SteamMarketReceiptKind.Cancelled, listingId, returnedContext, returned, buyer: 0, seller: 0, at);
            }
            case 3 or 4:
            {
                if (purchaseId is null || !purchases.TryGetProperty(listingId + "_" + purchaseId, out JsonElement purchase) ||
                    purchase.ValueKind != JsonValueKind.Object)
                {
                    reason = "Reference:Purchase" + kind;

                    return null;
                }

                if (!Zero(purchase, "failed") || !Zero(purchase, "needs_rollback") || !Zero(purchase, "funds_returned"))
                {
                    reason = "Unsettled";

                    return null;
                }

                return type == 3
                    ? Sale(id, listingId, listed, purchase, assets, currency, fees, at, kind, out reason)
                    : Purchase(id, listed, purchase, assets, currency, at, kind, out reason);
            }
            default:
                reason = "EventType";

                return null;
        }
    }

    private static SteamMarketReceipt? Sale(
        string id,
        string listingId,
        JsonElement listed,
        JsonElement purchase,
        JsonElement assets,
        SteamCurrency currency,
        SellerFeeSchedule fees,
        DateTimeOffset at,
        string kind,
        out string reason)
    {
        if (Asset(listed, assets, fallback: null, out SteamItemReference item, out _) is { } failure)
        {
            reason = failure + kind;

            return null;
        }

        if (Whole(purchase, "received_currencyid") != PurchaseCurrencyOffset + currency.SteamId)
        {
            reason = "Currency";

            return null;
        }

        if (Whole(purchase, "received_amount") is not ({ } received and > 0) || received % item.Quantity != 0)
        {
            reason = "Money";

            return null;
        }

        try
        {
            // Steam reports only the proceeds; the buyer price follows from the fee schedule, per item.
            decimal each = fees.BuyerPrice(received * currency.MinorUnit / item.Quantity, currency.Precision);
            reason = string.Empty;

            return item.Receipt(
                id,
                SteamMarketReceiptKind.Sold,
                listingId,
                item.ContextId,
                item.AssetId,
                (long)(each / currency.MinorUnit) * item.Quantity,
                received,
                at);
        }
        catch (TradingInvariantException)
        {
            reason = "Money";

            return null;
        }
    }

    private static SteamMarketReceipt? Purchase(
        string id,
        JsonElement listed,
        JsonElement purchase,
        JsonElement assets,
        SteamCurrency currency,
        DateTimeOffset at,
        string kind,
        out string reason)
    {
        if (!purchase.TryGetProperty("asset", out JsonElement bought) || bought.ValueKind != JsonValueKind.Object)
        {
            reason = "Reference:PurchaseAsset" + kind;

            return null;
        }

        if (Asset(bought, assets, listed, out SteamItemReference item, out _) is { } failure)
        {
            reason = failure + kind;

            return null;
        }

        string? newAsset = Id(Text(bought, "new_id"));
        string newContext = Id(Text(bought, "new_contextid")) ?? item.ContextId;

        if (Whole(purchase, "currencyid") != PurchaseCurrencyOffset + currency.SteamId)
        {
            reason = "Currency";

            return null;
        }

        if (newAsset is null)
        {
            reason = "Reference:NewAsset" + kind;

            return null;
        }

        if (Whole(purchase, "paid_amount") is not ({ } paid and > 0) || Whole(purchase, "paid_fee") is not { } fee)
        {
            reason = "Money";

            return null;
        }

        // The buy order is not named: the receipt stays unattributed until the journal proves whose buy it was.
        reason = string.Empty;

        return item.Receipt(id, SteamMarketReceiptKind.Bought, string.Empty, newContext, newAsset, paid + fee, paid, at);
    }

    /// <summary>
    ///     Resolves an asset to its description: listed items under their own context and ID, bought items also under their
    ///     new ones, and, as a live account showed, under a unique match by ID across the app's contexts. Null when it
    ///     resolves, otherwise what failed.
    /// </summary>
    private static string? Asset(JsonElement asset, JsonElement assets, JsonElement? fallback, out SteamItemReference item, out JsonElement description)
    {
        item = default;
        description = default;
        string? app = Text(asset, "appid");
        string? context = Id(Text(asset, "contextid"));
        string? id = Id(Text(asset, "id"));

        if (app is null || context is null || id is null || !int.TryParse(app, NumberStyles.None, CultureInfo.InvariantCulture, out int appId) || appId <= 0)
        {
            return "Asset:Ids";
        }

        if (!assets.TryGetProperty(app, out JsonElement byContext) || byContext.ValueKind != JsonValueKind.Object)
        {
            return "Asset:Map";
        }

        string? newId = Id(Text(asset, "new_id"));
        string newContext = Id(Text(asset, "new_contextid")) ?? context;

        if (!Entry(byContext, context, id, out description) && (newId is null || !Entry(byContext, newContext, newId, out description)) &&
            !UniqueEntry(byContext, id, newId, out description))
        {
            return byContext.TryGetProperty(context, out _) || byContext.TryGetProperty(newContext, out _) ? "Asset:Entry" : "Asset:Map";
        }

        // A moved asset reports amount 0; the traded amount then comes from the original amount or the listing.
        long? amount = Positive(Whole(asset, "amount")) ?? Positive(Whole(description, "original_amount")) ??
            (fallback is { } listed ? Positive(Whole(listed, "amount")) : null);

        if (amount is null or > int.MaxValue)
        {
            return "Asset:Amount";
        }

        if (Text(description, "market_hash_name") is not { Length: > 0 } name)
        {
            return "Asset:Instrument";
        }

        item = new SteamItemReference(appId, name, context, id, (int)amount.Value);

        return null;
    }

    private static bool Entry(JsonElement byContext, string context, string id, out JsonElement description)
    {
        description = default;

        return byContext.TryGetProperty(context, out JsonElement byId) && byId.ValueKind == JsonValueKind.Object && byId.TryGetProperty(id, out description) &&
            description.ValueKind == JsonValueKind.Object;
    }

    private static bool UniqueEntry(JsonElement byContext, string id, string? newId, out JsonElement description)
    {
        JsonElement[] matches =
        [
            .. byContext.EnumerateObject()
                .Where(pair => pair.Value.ValueKind == JsonValueKind.Object)
                .SelectMany(pair => pair.Value.EnumerateObject())
                .Where(pair => (pair.Name == id || pair.Name == newId) && pair.Value.ValueKind == JsonValueKind.Object)
                .Select(pair => pair.Value)
        ];
        description = matches.Length == 1 ? matches[0] : default;

        return matches.Length == 1;
    }

    private static bool Success(JsonElement root)
    {
        return root.TryGetProperty("success", out JsonElement success) &&
            (success.ValueKind == JsonValueKind.True || (success.ValueKind == JsonValueKind.Number && success.TryGetInt32(out int code) && code == 1));
    }

    private static bool Zero(JsonElement row, string name)
    {
        return !row.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.False or JsonValueKind.Null || Whole(row, name) == 0;
    }

    private static JsonElement Map(JsonElement root, string name)
    {
        // Steam writes an empty map as an empty array.
        return root.TryGetProperty(name, out JsonElement map) && map.ValueKind == JsonValueKind.Object ? map : EmptyMap;
    }

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static long? Whole(JsonElement element, string name)
    {
        return Text(element, name) is { Length: > 0 and <= 19 } text && text.All(char.IsAsciiDigit) &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
                ? value
                : null;
    }

    private static long? Positive(long? value)
    {
        return value > 0 ? value : null;
    }

    private static string? Id(string? value)
    {
        return value is { Length: > 0 and <= 20 } && value.All(char.IsAsciiDigit) ? value : null;
    }
}