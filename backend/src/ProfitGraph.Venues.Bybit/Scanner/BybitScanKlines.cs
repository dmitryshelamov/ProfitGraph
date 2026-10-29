using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Venues.Bybit.Scanner;

/// <summary>
///     The public Spot 1-minute kline topics of the market scanner and the candles their messages carry, closed or forming,
///     with turnover. Messages of other topics, subscription answers and pongs are ignored.
/// </summary>
internal static class BybitScanKlines
{
    private const string TopicPrefix = "kline.1.";

    internal static string Topic(string symbol)
    {
        return TopicPrefix + symbol;
    }

    internal static IReadOnlyList<ScanCandleUpdateInternal> Read(string message, IReadOnlySet<string> symbols)
    {
        using JsonDocument document = JsonDocument.Parse(message);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("topic", out JsonElement topic) || topic.GetString() is not { } name ||
            !name.StartsWith(TopicPrefix, StringComparison.Ordinal) || !symbols.Contains(name[TopicPrefix.Length..]) ||
            !root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        string symbol = name[TopicPrefix.Length..];
        var updates = new List<ScanCandleUpdateInternal>();

        foreach (JsonElement row in data.EnumerateArray())
        {
            bool closed = row.TryGetProperty("confirm", out JsonElement confirm) && confirm.ValueKind == JsonValueKind.True;
            updates.Add(new ScanCandleUpdateInternal(symbol, Candle(row), closed));
        }

        return [.. updates.OrderBy(update => update.Candle.OpenTime)];
    }

    private static ScanCandle Candle(JsonElement row)
    {
        DateTimeOffset openTime = DateTimeOffset.FromUnixTimeMilliseconds(row.GetProperty("start").GetInt64());

        var candle = new ScanCandle(
            openTime,
            Decimal(row, "open"),
            Decimal(row, "high"),
            Decimal(row, "low"),
            Decimal(row, "close"),
            Decimal(row, "volume"),
            Decimal(row, "turnover"));

        if (openTime.ToUnixTimeMilliseconds() % 60_000 != 0 || candle.Open <= 0m || candle.Low <= 0m || candle.Close <= 0m ||
            candle.High < Math.Max(candle.Open, candle.Close) || candle.Low > Math.Min(candle.Open, candle.Close) || candle.Volume < 0m ||
            candle.Turnover < 0m)
        {
            throw new InvalidDataException("A scanner kline is misaligned or has invalid prices, volume or turnover.");
        }

        return candle;
    }

    private static decimal Decimal(JsonElement row, string name)
    {
        string? text = row.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : throw new InvalidDataException("A scanner kline decimal is missing or invalid.");
    }
}