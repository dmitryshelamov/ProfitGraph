using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Charts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner;

/// <summary>
///     The trade cards' proposals for a pair on every timeframe of <see cref="ScanTradeOptions.LevelTimeframes" /> the venue offers
///     (ADR 0060): each timeframe's levels at the pair's price give its own levels and 2R trades; the pullback comes once, from the last
///     hour of the trades' own candles, under <see cref="PullbackTimeframe" />, and the trades beyond the extreme of the last four hours
///     of those candles once, under <see cref="ExtremeTimeframe" />, with the hourly average range as their buffer (ADR 0072). A trade
///     that another timeframe already proposes with the same prices is listed once, under the shorter timeframe. Without a price there
///     are no proposals. On a venue that allows shorts the short proposals come with the long ones (ADR 0061). A stop at a level lies
///     beyond it by a quarter of that timeframe's average range, and an entry at the price with a stop nearer than one range is left out
///     (ADR 0070).
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
    public const string PullbackTimeframe = ScanTradeIdeas.PullbackTimeframe;

    /// <summary>
    ///     The timeframe a trade beyond the four hours' extreme is shown and stored under: its range is the last four hours.
    /// </summary>
    public const string ExtremeTimeframe = ScanTradeIdeas.ExtremeTimeframe;

    /// <summary>
    ///     The timeframe whose average range buffers a stop beyond the four hours' extreme, as the preset backtest's hours do.
    /// </summary>
    private const string RangeTimeframe = "1h";

    private static readonly TimeSpan LastHour = TimeSpan.FromHours(1);

    public async Task<(decimal? Price, IReadOnlyList<(string Timeframe, ScanTradeIdea Idea)> Ideas)> Read(
        string venue,
        string symbol,
        CancellationToken cancellationToken)
    {
        HashSet<string> offered = [.. venues.Find(venue)?.Timeframes.Select(timeframe => timeframe.Key) ?? []];
        bool shorts = venues.Find(venue) is { SupportsShort: true };
        decimal? price = null;
        decimal? hourRange = null;
        List<ScanTimeframeLevels> frames = [];

        foreach (string timeframe in options.LevelTimeframes.Where(offered.Contains))
        {
            ScanLevelsInternal read = await levels.Read(venue, symbol, timeframe, cancellationToken);
            price ??= read.Price;
            hourRange = timeframe == RangeTimeframe ? read.AverageRange : hourRange;

            if (read.Price is { } current)
            {
                frames.Add(new ScanTimeframeLevels(timeframe, current, read.Support, read.Resistance, read.AverageRange));
            }
        }

        if (price is not { } now)
        {
            return (null, []);
        }

        (decimal Low, decimal High)? lastHour = null;
        (decimal Low, decimal High)? lastFourHours = null;

        if (offered.Contains(PullbackTimeframe))
        {
            bool extremes = offered.Contains(ExtremeTimeframe) && offered.Contains(RangeTimeframe);

            if (extremes && !options.LevelTimeframes.Contains(RangeTimeframe))
            {
                hourRange = (await levels.Read(venue, symbol, RangeTimeframe, cancellationToken)).AverageRange;
            }

            CandleSeries series = venues.ResolveSeries(venue, symbol, options.Timeframe);
            DateTimeOffset end = series.Timeframe.FloorToGrid(clock.GetUtcNow());
            using IDisposable reads = MarketDataAccess.Use(await access.Read(venue, cancellationToken));
            TimeSpan window = ScanTradeIdeas.ExtremeWindow;
            CandleChartPage page = await chart.Read(series, end - window, end, Math.Max((int)(window / series.Timeframe.Duration), val2: 1), cancellationToken);
            Candle[] hour = [.. page.Candles.Where(candle => candle.OpenTime >= end - LastHour)];
            lastHour = hour.Length > 0 ? (hour.Min(candle => candle.Low), hour.Max(candle => candle.High)) : null;
            lastFourHours = extremes && page.Candles.Count > 0 ? (page.Candles.Min(candle => candle.Low), page.Candles.Max(candle => candle.High)) : null;
        }

        return (now, ScanTradeIdeas.Compose(now, frames, lastHour, lastFourHours, hourRange, shorts));
    }
}