using System.Globalization;
using System.Text;
using System.Text.Json;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     Reads <c>g_rgWalletInfo</c> from the market page (Profit Fox Designer ADR 0082): currency number, balance in minor
///     units, Steam's and the publisher's fee rates, the market minimum and the currency increment, the step of every amount
///     the wallet accepts (a hundred minor units for KZT, whose prices are whole tenge). A currency without a known minor
///     unit is refused rather than guessed.
/// </summary>
internal static class SteamWalletParser
{
    public static SteamWallet Read(byte[] html)
    {
        string text = Encoding.UTF8.GetString(html);
        int name = text.IndexOf("g_rgWalletInfo", StringComparison.Ordinal);
        int start = name < 0 ? -1 : text.IndexOf(value: '{', name);

        if (start < 0)
        {
            throw Invalid("it carries no g_rgWalletInfo");
        }

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text[start..]));
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            decimal id = Number(root, "wallet_currency");

            if (id != decimal.Truncate(id) || id is <= 0 or > int.MaxValue || SteamCurrency.FindBySteamId((int)id) is not { } currency)
            {
                throw new SteamMarketDataException(unavailable: false, "The Steam wallet's currency is not supported.");
            }

            decimal balance = Number(root, "wallet_balance");
            decimal minimum = Limit(root, "wallet_market_minimum");
            decimal increment = Limit(root, "wallet_currency_increment");

            if (balance < 0 || balance != decimal.Truncate(balance))
            {
                throw Invalid("its balance is not whole minor units");
            }

            if (increment != 1)
            {
                throw Invalid($"its currency increment is {increment.ToString(CultureInfo.InvariantCulture)} minor units, not one");
            }

            return new SteamWallet(
                currency,
                balance * currency.MinorUnit,
                new SellerFeeSchedule(
                    [Number(root, "wallet_fee_percent"), Number(root, "wallet_publisher_fee_percent_default")],
                    minimum * currency.MinorUnit,
                    checked((long)increment)));
        }
        catch (Exception exception) when (exception is JsonException or TradingInvariantException or OverflowException)
        {
            throw Invalid($"its wallet is unreadable ({exception.GetType().Name})");
        }
    }

    private static decimal Number(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out JsonElement value))
        {
            throw Invalid($"its wallet has no {field}");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal text) => text,
            _ => throw Invalid($"its wallet's {field} is not a number")
        };
    }

    /// <summary>
    ///     A limit in minor units; Steam's own pages fall back to one when it is absent.
    /// </summary>
    private static decimal Limit(JsonElement root, string field)
    {
        decimal value = root.TryGetProperty(field, out _) ? Number(root, field) : 1;

        return value >= 1 && value == decimal.Truncate(value) ? value : throw Invalid($"its wallet's {field} is not a whole positive number");
    }

    /// <summary>
    ///     The refusal names what is wrong, never a value of the account's own, such as its balance.
    /// </summary>
    private static SteamMarketDataException Invalid(string reason)
    {
        return new SteamMarketDataException(unavailable: false, $"The Steam market page has no readable wallet: {reason}.");
    }
}