using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner;

/// <summary>
///     The trade cards' starting point for a pair (ADR 0060): its row of the current snapshot, and the ideas from that row's price and
///     levels and the lowest low of the last hour's closed minutes. A pair the snapshot does not show has no ideas.
/// </summary>
public sealed class ScanTradeIdeasReader(
    IScanSnapshotRepository snapshots,
    VenueRegistry venues,
    CandleChartReader chart,
    ScanTradeOptions options,
    TimeProvider clock)
{
    private static readonly TimeSpan LastHour = TimeSpan.FromHours(1);

    public async Task<(ScanSnapshotRowInternal? Row, DateTimeOffset? ComputedAt, IReadOnlyList<ScanTradeIdea> Ideas)> Read(
        string venue,
        string symbol,
        CancellationToken cancellationToken)
    {
        ScanSnapshotInternal? snapshot = await snapshots.Get(venue, cancellationToken);
        ScanSnapshotRowInternal? row = snapshot?.Rows.FirstOrDefault(item => item.Symbol == symbol);

        if (row?.Metrics.Price is not { } price)
        {
            return (row, snapshot?.ComputedAt, []);
        }

        CandleSeries series = venues.ResolveSeries(venue, symbol, options.Timeframe);
        DateTimeOffset end = series.Timeframe.FloorToGrid(clock.GetUtcNow());
        CandleChartPage page = await chart.Read(series, end - LastHour, end, Math.Max((int)(LastHour / series.Timeframe.Duration), val2: 1), cancellationToken);
        decimal? low = page.Candles.Count > 0 ? page.Candles.Min(candle => candle.Low) : null;

        return (row, snapshot!.ComputedAt, ScanTradeIdeas.Of(price, row.Metrics.Support, row.Metrics.Resistance, low));
    }
}