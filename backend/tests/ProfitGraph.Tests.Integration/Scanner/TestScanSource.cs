using System.Runtime.CompilerServices;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     A scanner on the test history venue, so the scanner's reads of that venue's history can be tested without Bybit. It has
///     no pairs and no presets.
/// </summary>
public sealed class TestScanSource : IMarketScanSource
{
    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public int MaxSymbolsPerStream => 1;

    public IReadOnlyList<ScanPresetInternal> BuiltInPresets => [];

    public Task<IReadOnlyList<ScanInstrumentInternal>> Universe(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ScanInstrumentInternal>>([]);
    }

    public Task<IReadOnlyList<ScanCandle>> Candles(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ScanCandle>>([]);
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
}