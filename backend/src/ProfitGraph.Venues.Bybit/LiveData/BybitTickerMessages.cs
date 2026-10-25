using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     Bybit's public Spot <c>tickers.{symbol}</c> messages: the last-trade price and the message time.
/// </summary>
internal static class BybitTickerMessages
{
    public static string Topic(string symbol)
    {
        return $"tickers.{symbol}";
    }

    /// <summary>
    ///     The price a ticker message of <paramref name="topic" /> reports, or null for any other message.
    /// </summary>
    public static LiveMarkPriceInternal? Price(string message, string topic)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("topic", out JsonElement kind) || kind.GetString() != topic ||
                !root.TryGetProperty("ts", out JsonElement time) || !time.TryGetInt64(out long milliseconds) ||
                !root.TryGetProperty("data", out JsonElement data) || !data.TryGetProperty("lastPrice", out JsonElement last) ||
                !decimal.TryParse(last.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal price) || price <= 0m)
            {
                return null;
            }

            return new LiveMarkPriceInternal(price, DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}