using System.Text.Json;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     Reads an item's order book (<c>/market/orderbook?q=Load</c>, as Profit Fox Designer read it after Steam dropped item IDs
///     from listing pages): <c>data.data.eCurrency</c> must be the wallet's currency and <c>amtMaxBuyOrder</c> is the highest
///     buy order in whole minor units.
/// </summary>
internal static class SteamOrderBookParser
{
    public static decimal? HighestBuy(byte[] body, SteamCurrency currency)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("data", out JsonElement envelope) || !IsSuccess(envelope) ||
                !envelope.TryGetProperty("data", out JsonElement book) || !book.TryGetProperty("eCurrency", out JsonElement id) ||
                !id.TryGetInt32(out int currencyId))
            {
                throw Invalid();
            }

            if (currencyId != currency.SteamId)
            {
                throw new SteamMarketDataException(unavailable: false, "Steam's order book is not in the wallet's currency.");
            }

            if (!book.TryGetProperty("amtMaxBuyOrder", out JsonElement highest) || highest.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            decimal units = highest.GetDecimal();

            return units > 0m && units == decimal.Truncate(units) ? units * currency.MinorUnit : throw Invalid();
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            throw Invalid();
        }
    }

    private static bool IsSuccess(JsonElement element)
    {
        return element.TryGetProperty("success", out JsonElement success) &&
            (success.ValueKind == JsonValueKind.True || (success.ValueKind == JsonValueKind.Number && success.GetInt32() == 1));
    }

    private static SteamMarketDataException Invalid()
    {
        return new SteamMarketDataException(unavailable: false, "Steam's order book is unreadable.");
    }
}