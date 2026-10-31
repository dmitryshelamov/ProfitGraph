using System.Runtime.CompilerServices;

using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     A scanner on the test history venue for backtests, with invented minutes: <c>GOODUSDT</c> swings around 100 except for one hour,
///     ten days before the end of the requested range, in which it climbs to 105 and swings around that; <c>BADUSDT</c>'s history cannot
///     be read.
/// </summary>
public sealed class BacktestScanSource : IMarketScanSource
{
    public const string Good = "GOODUSDT";
    public const string Bad = "BADUSDT";

    /// <summary>
    ///     How far every minute trades beyond its open and close, so that an entry at a minute's close fills in the next one.
    /// </summary>
    private const decimal Wick = 0.05m;

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public int MaxSymbolsPerStream => 1;

    public IReadOnlyList<ScanPresetInternal> BuiltInPresets => [];

    public Task<IReadOnlyList<ScanInstrumentInternal>> Universe(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ScanInstrumentInternal>>(
            [new ScanInstrumentInternal(Bad, ListedAt: null), new ScanInstrumentInternal(Good, ListedAt: null)]);
    }

    public Task<IReadOnlyList<ScanCandle>> Candles(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (symbol.Value == Bad)
        {
            throw new MarketScanException(MarketScanErrorCodes.ProviderUnavailable, "Invented outage.");
        }

        DateTimeOffset climb = ClimbStart(to);
        List<ScanCandle> candles = [];
        decimal close = Level(from - TimeSpan.FromMinutes(1), climb);

        for (DateTimeOffset openTime = from; openTime < to; openTime += TimeSpan.FromMinutes(1))
        {
            decimal open = close;
            close = Level(openTime, climb);
            candles.Add(new ScanCandle(openTime, open, Math.Max(open, close) + Wick, Math.Min(open, close) - Wick, close, Volume: 1, close));
        }

        return Task.FromResult<IReadOnlyList<ScanCandle>>(candles);
    }

    public Task<IReadOnlyList<ScanQuoteInternal>> Quotes(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ScanQuoteInternal>>([]);
    }

    public async IAsyncEnumerable<ScanCandleUpdateInternal> Stream(
        IReadOnlyList<InstrumentSymbol> symbols,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);

        yield break;
    }

    /// <summary>
    ///     The close of the minute at <paramref name="openTime" />: 100 before the climb, 105 after it, rising evenly within its hour,
    ///     with a swing of 0.4 every four hours that gives the cards levels and a range (ADR 0073) and never moves the price 3 % in an
    ///     hour.
    /// </summary>
    private static decimal Level(DateTimeOffset openTime, DateTimeOffset climb)
    {
        var minute = (int)((openTime - climb).Ticks / TimeSpan.TicksPerMinute);
        decimal level = minute switch
        {
            < 0 => 100m,
            < 60 => 100m + 5m * (minute + 1) / 60m,
            _ => 105m
        };
        double wave = Math.Sin(2 * Math.PI * (openTime.UtcTicks / TimeSpan.TicksPerMinute % 240) / 240);

        return level + Math.Round(0.4m * (decimal)wave, decimals: 4);
    }

    /// <summary>
    ///     The first minute of the climb.
    /// </summary>
    public static DateTimeOffset ClimbStart(DateTimeOffset to)
    {
        return new DateTimeOffset(to.UtcTicks - to.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero).AddDays(-10);
    }
}