using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.RunScanBacktest;

internal static class ScanHistory
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The venue's minutes from the first one on, holes filled with flat candles at the previous close: a minute the venue reports
    ///     nowhere had no trades, as the live scanner treats it (ADR 0059).
    /// </summary>
    public static List<ScanCandle> Contiguous(IReadOnlyList<ScanCandle> candles)
    {
        var series = new List<ScanCandle>(candles.Count);

        foreach (ScanCandle candle in candles.OrderBy(candle => candle.OpenTime))
        {
            while (series.Count > 0 && series[^1].OpenTime + Minute < candle.OpenTime)
            {
                decimal close = series[^1].Close;
                series.Add(new ScanCandle(series[^1].OpenTime + Minute, close, close, close, close, Volume: 0, Turnover: 0));
            }

            if (series.Count == 0 || candle.OpenTime > series[^1].OpenTime)
            {
                series.Add(candle);
            }
        }

        return series;
    }
}