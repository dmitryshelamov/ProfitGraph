using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner;

/// <summary>
///     The trade cards' proposals for a pair on every timeframe of <see cref="ScanTradeOptions.LevelTimeframes" /> the venue offers
///     (ADR 0060): each timeframe's levels at the pair's price give its own levels and 2R trades; the pullback comes once, from the last
///     hour of the trades' own candles, under <see cref="PullbackTimeframe" />. A trade that another timeframe already proposes with
///     the same prices is listed once, under the shorter timeframe. Without a price there are no proposals. On a venue that allows
///     shorts the short proposals come with the long ones (ADR 0061). A stop at a level lies beyond it by a quarter of that timeframe's
///     average range, and an entry at the price with a stop nearer than one range is left out (ADR 0070).
/// </summary>
public sealed class ScanTradeIdeasReader(
    ScanTimeframeLevelsReader levels,
    VenueRegistry venues,
    CandleChartReader chart,
    ScanMarketDataAccess access,
    ScanTradeOptions options,
    TimeProvider clock)
{
    /// <summary>
    ///     The timeframe a pullback is shown and stored under: its range is the last hour.
    /// </summary>
    public const string PullbackTimeframe = "1h";

    private static readonly TimeSpan LastHour = TimeSpan.FromHours(1);

    public async Task<(decimal? Price, IReadOnlyList<(string Timeframe, ScanTradeIdea Idea)> Ideas)> Read(
        string venue,
        string symbol,
        CancellationToken cancellationToken)
    {
        HashSet<string> offered = [.. venues.Find(venue)?.Timeframes.Select(timeframe => timeframe.Key) ?? []];
        bool shorts = venues.Find(venue) is { SupportsShort: true };
        decimal? price = null;
        List<(string Timeframe, ScanTradeIdea Idea)> ideas = [];

        foreach (string timeframe in options.LevelTimeframes.Where(offered.Contains))
        {
            ScanLevelsInternal read = await levels.Read(venue, symbol, timeframe, cancellationToken);
            price ??= read.Price;

            if (read.Price is { } current)
            {
                ideas.AddRange(
                    ScanTradeIdeas.Of(current, read.Support, read.Resistance, lastHourLow: null, lastHourHigh: null, shorts, read.AverageRange)
                        .Select(idea => (timeframe, idea)));
            }
        }

        if (price is { } now && offered.Contains(PullbackTimeframe))
        {
            CandleSeries series = venues.ResolveSeries(venue, symbol, options.Timeframe);
            DateTimeOffset end = series.Timeframe.FloorToGrid(clock.GetUtcNow());
            using IDisposable reads = MarketDataAccess.Use(await access.Read(venue, cancellationToken));
            CandleChartPage page = await chart.Read(series, end - LastHour, end, Math.Max((int)(LastHour / series.Timeframe.Duration), val2: 1), cancellationToken);
            decimal? low = page.Candles.Count > 0 ? page.Candles.Min(candle => candle.Low) : null;
            decimal? high = page.Candles.Count > 0 ? page.Candles.Max(candle => candle.High) : null;
            ideas.AddRange(
                ScanTradeIdeas.Of(now, support: null, resistance: null, low, high, shorts)
                    .Select(idea => (PullbackTimeframe, idea)));
        }

        return (price, [.. ideas.DistinctBy(item => (item.Idea.Scenario, item.Idea.Direction, item.Idea.Entry, item.Idea.Stop, item.Idea.Target))]);
    }
}