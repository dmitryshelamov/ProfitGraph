using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Backtesting.History;

/// <summary>
///     Hashes the canonical content of prepared history: every part is length-prefixed UTF-8, times are UTC ticks and
///     decimals are exact text, so equal candles always give equal identities. The observation time is left out; the
///     latest closed boundary it implies is part of the partition.
/// </summary>
internal static class HistoryIdentityCalculator
{
    private const string Version = "profitgraph-history/v1";

    public static string Calculate(ResolvedCandleRange range, DateTimeOffset historyFrom, int warmupCount, IReadOnlyList<Candle> candles)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Version);
        Append(hash, range.Series.Venue.Value);
        Append(hash, range.Series.Symbol.Value);
        Append(hash, range.Series.Timeframe.Key);
        Append(hash, Ticks(historyFrom));
        Append(hash, Ticks(range.From));
        Append(hash, Ticks(range.To));
        Append(hash, Ticks(range.LatestClosed));
        Append(hash, warmupCount.ToString(CultureInfo.InvariantCulture));

        foreach (Candle candle in candles)
        {
            Append(hash, Ticks(candle.OpenTime));
            Append(hash, ExactDecimalText.Format(candle.Open));
            Append(hash, ExactDecimalText.Format(candle.High));
            Append(hash, ExactDecimalText.Format(candle.Low));
            Append(hash, ExactDecimalText.Format(candle.Close));
            Append(hash, ExactDecimalText.Format(candle.Volume));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string Ticks(DateTimeOffset time)
    {
        return time.UtcTicks.ToString(CultureInfo.InvariantCulture);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":"));
        hash.AppendData(bytes);
    }
}