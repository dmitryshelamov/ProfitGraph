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
///     A scanner on the test history venue for backtests, with invented minutes: <c>GOODUSDT</c> stays at 100 except for one hour, ten
///     days before the end of the requested range, in which it climbs to 105 and stays there; <c>BADUSDT</c>'s history cannot be read.
/// </summary>
public sealed class BacktestScanSource : IMarketScanSource
{
    public const string Good = "GOODUSDT";
    public const string Bad = "BADUSDT";

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
        var close = 100m;

        for (DateTimeOffset openTime = from; openTime < to; openTime += TimeSpan.FromMinutes(1))
        {
            decimal open = close;
            var minute = (int)((openTime - climb).Ticks / TimeSpan.TicksPerMinute);

            if (minute is >= 0 and < 60)
            {
                close = 100m + 5m * (minute + 1) / 60m;
            }

            candles.Add(new ScanCandle(openTime, open, Math.Max(open, close), Math.Min(open, close), close, Volume: 1, close));
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
    ///     The first minute of the climb.
    /// </summary>
    public static DateTimeOffset ClimbStart(DateTimeOffset to)
    {
        return new DateTimeOffset(to.UtcTicks - to.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero).AddDays(-10);
    }
}