using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     Reads the confirmed candles of a public Spot kline message; unconfirmed candles and other messages are ignored.
/// </summary>
internal static class BybitKlineMessages
{
    internal static string Topic(string symbol, Timeframe timeframe)
    {
        return $"kline.{Interval(timeframe)}.{symbol}";
    }

    internal static IReadOnlyList<Candle> Closed(string message, string topic, Timeframe timeframe)
    {
        using JsonDocument document = JsonDocument.Parse(message);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("topic", out JsonElement actual) || actual.GetString() != topic ||
            !root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var closed = new List<Candle>();

        foreach (JsonElement row in data.EnumerateArray())
        {
            if (!row.TryGetProperty("confirm", out JsonElement confirm) || confirm.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            closed.Add(Read(row, timeframe));
        }

        return [.. closed.OrderBy(candle => candle.OpenTime)];
    }

    /// <summary>
    ///     Reads every candle of a public Spot kline message, confirmed or still forming, for the live chart; other messages are
    ///     ignored. A candle's observation time is the message's own time when it carries one.
    /// </summary>
    internal static IReadOnlyList<FormingCandleInternal> Updates(string message, string topic, Timeframe timeframe)
    {
        using JsonDocument document = JsonDocument.Parse(message);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("topic", out JsonElement actual) || actual.GetString() != topic ||
            !root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        DateTimeOffset? sent = root.TryGetProperty("ts", out JsonElement ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out long milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;
        var updates = new List<FormingCandleInternal>();

        foreach (JsonElement row in data.EnumerateArray())
        {
            Candle candle = Read(row, timeframe);
            bool closed = row.TryGetProperty("confirm", out JsonElement confirm) && confirm.ValueKind == JsonValueKind.True;
            updates.Add(new FormingCandleInternal(candle.OpenTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, closed, sent ?? candle.OpenTime));
        }

        return [.. updates.OrderBy(update => update.OpenTime)];
    }

    internal static bool Supports(Timeframe timeframe)
    {
        try
        {
            Interval(timeframe);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static Candle Read(JsonElement row, Timeframe timeframe)
    {
        DateTimeOffset openTime = DateTimeOffset.FromUnixTimeMilliseconds(row.GetProperty("start").GetInt64());

        if (!timeframe.IsAligned(openTime))
        {
            throw new InvalidDataException("A live kline is not aligned to its timeframe.");
        }

        var candle = new Candle(openTime, Decimal(row, "open"), Decimal(row, "high"), Decimal(row, "low"), Decimal(row, "close"), Decimal(row, "volume"));

        if (candle.Open <= 0m || candle.Low <= 0m || candle.High < Math.Max(candle.Open, candle.Close) || candle.Low > Math.Min(candle.Open, candle.Close) ||
            candle.Volume < 0m)
        {
            throw new InvalidDataException("A live kline has invalid prices or volume.");
        }

        return candle;
    }

    private static decimal Decimal(JsonElement row, string name)
    {
        string? text = row.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : throw new InvalidDataException("A live kline decimal is missing or invalid.");
    }

    private static string Interval(Timeframe timeframe)
    {
        return timeframe.Key switch
        {
            "1m" => "1",
            "3m" => "3",
            "5m" => "5",
            "15m" => "15",
            "30m" => "30",
            "1h" => "60",
            "2h" => "120",
            "4h" => "240",
            "6h" => "360",
            "12h" => "720",
            "1d" => "D",
            _ => throw new ArgumentException("Bybit has no live kline for this timeframe.", nameof(timeframe))
        };
    }
}