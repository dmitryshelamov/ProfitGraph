using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Tests.Unit.Infrastructure.Scanner;

/// <summary>
///     A scan source the test drives: it records every call in order, answers history with flat candles of the asked range and
///     streams what the test pushes on the current connection.
/// </summary>
internal sealed class FakeMarketScanSource(params string[] universe) : IMarketScanSource
{
    private Channel<ScanCandleUpdateInternal> _current = Channel.CreateUnbounded<ScanCandleUpdateInternal>();

    public ConcurrentQueue<string> Calls { get; } = new ConcurrentQueue<string>();

    public VenueId Venue => new VenueId("bybit");

    public int MaxSymbolsPerStream => 100;

    public IReadOnlyList<ScanPresetInternal> BuiltInPresets => [];

    public Task<IReadOnlyList<ScanInstrumentInternal>> Universe(CancellationToken cancellationToken)
    {
        Calls.Enqueue("universe");

        return Task.FromResult<IReadOnlyList<ScanInstrumentInternal>>([.. universe.Select(symbol => new ScanInstrumentInternal(symbol, ListedAt: null))]);
    }

    public Task<IReadOnlyList<ScanCandle>> Candles(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        Calls.Enqueue($"candles {symbol.Value} {timeframe.Key} {from:HH:mm}-{to:HH:mm}");
        var candles = new List<ScanCandle>();

        for (DateTimeOffset time = from; time < to; time += timeframe.Duration)
        {
            candles.Add(new ScanCandle(time, Open: 100, High: 100, Low: 100, Close: 100, Volume: 1, Turnover: 1));
        }

        return Task.FromResult<IReadOnlyList<ScanCandle>>(candles);
    }

    public Task<IReadOnlyList<ScanQuoteInternal>> Quotes(CancellationToken cancellationToken)
    {
        Calls.Enqueue("quotes");

        return Task.FromResult<IReadOnlyList<ScanQuoteInternal>>([.. universe.Select(symbol => new ScanQuoteInternal(symbol, BestBid: 99, BestAsk: 101))]);
    }

    public async IAsyncEnumerable<ScanCandleUpdateInternal> Stream(
        IReadOnlyList<InstrumentSymbol> symbols,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<ScanCandleUpdateInternal>();
        Interlocked.Exchange(ref _current, channel);
        Calls.Enqueue($"stream {string.Join(",", symbols.Select(symbol => symbol.Value))}");

        await foreach (ScanCandleUpdateInternal update in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
        }
    }

    public void Push(ScanCandleUpdateInternal update)
    {
        Volatile.Read(ref _current).Writer.TryWrite(update);
    }

    /// <summary>
    ///     Ends the current connection as a lost one.
    /// </summary>
    public void Drop()
    {
        Volatile.Read(ref _current).Writer.TryComplete(new IOException("The connection was lost."));
    }
}