using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner;

/// <summary>
///     A pair's levels on one timeframe (ADR 0059, section 8): its last <see cref="ScanLevels.MaximumCandles" /> closed candles of the
///     timeframe read through the shared candle cache, their levels applied at the price of the pair's snapshot row, or at the last
///     close when the snapshot lacks the pair. The pair page's levels and the trade cards (ADR 0060) read the same.
/// </summary>
public sealed class ScanTimeframeLevelsReader(VenueRegistry venues, CandleChartReader chart, IScanSnapshotRepository snapshots, TimeProvider clock)
{
    public async Task<ScanLevelsInternal> Read(string venue, string symbol, string timeframe, CancellationToken cancellationToken)
    {
        CandleSeries series = venues.ResolveSeries(venue, symbol, timeframe);
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset from = now - series.Timeframe.Duration * ScanLevels.MaximumCandles;
        CandleChartPage page = await chart.Read(series, from, now, ScanLevels.MaximumCandles, cancellationToken);
        List<ScanCandle> candles =
        [
            .. page.Candles.Select(candle => new ScanCandle(
                candle.OpenTime,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume,
                Turnover: 0))
        ];
        ScanSnapshotInternal? snapshot = await snapshots.Get(series.Venue.Value, cancellationToken);
        decimal? price = snapshot?.Rows.FirstOrDefault(row => row.Symbol == series.Symbol.Value)?.Metrics.Price ?? candles.LastOrDefault()?.Close;
        IReadOnlyList<decimal> levels = ScanLevels.Of(candles);
        ScanMetrics metrics = ScanLevels.Apply(new ScanMetrics(), levels, price);

        return new ScanLevelsInternal(
            series.Venue.Value,
            series.Symbol.Value,
            series.Timeframe.Key,
            page.From,
            page.To,
            page.Status,
            page.ErrorCode,
            candles.Count,
            price,
            levels,
            metrics.Support,
            metrics.Resistance,
            metrics.DistanceToSupport,
            metrics.DistanceToResistance,
            metrics.RewardRisk);
    }
}