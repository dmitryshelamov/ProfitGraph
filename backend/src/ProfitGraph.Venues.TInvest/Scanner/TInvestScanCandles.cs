using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Venues.TInvest.MarketData;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Scanner;

/// <summary>
///     A T-Invest candle as a scanner candle (ADR 0062): volume in lots becomes shares, and turnover, which T-Invest candles do not
///     carry, is the volume times the typical price (high + low + close) / 3 in roubles. A candle off its grid or inconsistent is
///     null.
/// </summary>
internal static class TInvestScanCandles
{
    public static readonly Timeframe Minute = Timeframe.Parse("1m");

    public static ScanCandle? From(
        DateTimeOffset openTime,
        Quotation? open,
        Quotation? high,
        Quotation? low,
        Quotation? close,
        long lots,
        TInvestShare share,
        Timeframe timeframe)
    {
        if (open is null || high is null || low is null || close is null || lots < 0)
        {
            return null;
        }

        decimal volume = lots * share.Lot;
        var h = high.ToDecimal();
        var l = low.ToDecimal();
        var c = close.ToDecimal();
        var candle = new ScanCandle(openTime, open.ToDecimal(), h, l, c, volume, volume * (h + l + c) / 3m);

        bool valid = timeframe.IsAligned(candle.OpenTime) && candle.Open > 0m && candle.Low > 0m && candle.Close > 0m &&
            candle.High >= Math.Max(candle.Open, candle.Close) && candle.Low <= Math.Min(candle.Open, candle.Close);

        return valid ? candle : null;
    }
}