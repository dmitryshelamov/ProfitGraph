using System.Globalization;
using System.Text.Json;

using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     Turns <c>/market/pricehistory</c> into median candles by the rules of Profit Fox Designer ADR 0081
///     ("Market-data model", with its 2026-10-24 observations). Every point is a median price and a count of sold items, so a
///     candle has open, high, low and close equal to the median. Older points are daily (stamped 01:00 UTC, normalized to
///     00:00), recent ones hourly within a rolling 720-hour zone. The 01:00 point of the zone's first day is partial and
///     dropped; inside the zone a complete UTC day gets the volume-weighted mean of its hourly medians. Only closed periods
///     are kept. The prices must be in the wallet's currency.
/// </summary>
internal static class SteamPriceHistoryParser
{
    private static readonly Timeframe Day = Timeframe.Parse("1d");
    private static readonly Timeframe Hour = Timeframe.Parse("1h");

    private static readonly string[] DateFormats = ["MMM d yyyy H: '+0'", "MMM dd yyyy H: '+0'", "MMM d yyyy HH: '+0'", "MMM dd yyyy HH: '+0'"];

    public static SteamPriceHistory Read(byte[] payload, SteamCurrency currency, DateTimeOffset observedAt, TimeSpan closeDelay)
    {
        try
        {
            return Parse(payload, currency, observedAt, closeDelay);
        }
        catch (Exception exception) when (exception is JsonException or OverflowException or InvalidOperationException)
        {
            throw Invalid();
        }
    }

    private static SteamPriceHistory Parse(byte[] payload, SteamCurrency currency, DateTimeOffset observedAt, TimeSpan closeDelay)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out JsonElement success) ||
            success.ValueKind is not (JsonValueKind.True or JsonValueKind.Number) || (success.ValueKind == JsonValueKind.Number && success.GetInt32() != 1) ||
            !root.TryGetProperty("prices", out JsonElement points) || points.ValueKind != JsonValueKind.Array)
        {
            throw Invalid();
        }

        if (!Text(root, "price_prefix").Equals(currency.Prefix, StringComparison.Ordinal) ||
            !Text(root, "price_suffix").Equals(currency.Suffix, StringComparison.Ordinal))
        {
            throw new SteamMarketDataException(unavailable: false, "Steam's history is not in the wallet's currency.");
        }

        var parsed = new List<Candle>();

        foreach (JsonElement point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 3 || point[0].ValueKind != JsonValueKind.String ||
                point[2].ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParseExact(
                    point[0].GetString(),
                    DateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset time) ||
                (parsed.Count > 0 && time <= parsed[^1].OpenTime) ||
                point[1].ValueKind != JsonValueKind.Number || !point[1].TryGetDecimal(out decimal median) || median <= 0 ||
                !decimal.TryParse(point[2].GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out decimal sold) || sold <= 0)
            {
                throw Invalid();
            }

            parsed.Add(new Candle(time, median, median, median, median, sold));
        }

        DateTimeOffset windowStart = observedAt.AddDays(-30);
        DateTimeOffset boundary = Hour.FloorToGrid(windowStart);

        if (boundary < windowStart)
        {
            boundary += Hour.Duration;
        }

        DateTimeOffset hourlyStart = parsed.Where(point => point.OpenTime.Hour != 1).Select(point => point.OpenTime).Prepend(boundary).Min();
        DateTimeOffset boundaryDay = Day.FloorToGrid(hourlyStart);
        DateTimeOffset closedHour = Hour.FloorToGrid(observedAt - closeDelay);
        DateTimeOffset closedDay = Day.FloorToGrid(observedAt - closeDelay);
        Candle[] hourly =
        [
            .. parsed.Where(point => point.OpenTime >= hourlyStart && !IsBoundaryDayPoint(point, boundaryDay) && point.OpenTime < closedHour)
        ];
        IEnumerable<Candle> native = parsed.Where(point => point.OpenTime < hourlyStart && !IsBoundaryDayPoint(point, boundaryDay))
            .Select(point => point with { OpenTime = Day.FloorToGrid(point.OpenTime) });
        IEnumerable<Candle> estimated = hourly.Where(point => point.OpenTime < closedDay)
            .GroupBy(point => Day.FloorToGrid(point.OpenTime))
            .Where(group => group.Key > boundaryDay)
            .Select(group =>
            {
                decimal volume = group.Sum(point => point.Volume);
                decimal estimate = group.Sum(point => point.Close * point.Volume) / volume;

                return new Candle(group.Key, estimate, estimate, estimate, estimate, volume);
            });

        return new SteamPriceHistory([.. native.Concat(estimated).Where(candle => candle.OpenTime < closedDay)], hourly, hourlyStart);
    }

    private static bool IsBoundaryDayPoint(Candle point, DateTimeOffset boundaryDay)
    {
        return point.OpenTime.Hour == 1 && Day.FloorToGrid(point.OpenTime) == boundaryDay;
    }

    private static string Text(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "\0";
    }

    private static SteamMarketDataException Invalid()
    {
        return new SteamMarketDataException(unavailable: false, "Steam's price history is unreadable.");
    }
}