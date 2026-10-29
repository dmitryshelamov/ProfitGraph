using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Ports;

/// <summary>
///     A venue's public market data for the market scanner (ADR 0059): its scan universe, closed candles with turnover, a quote
///     snapshot and a stream of 1-minute candles. A venue without this port has no scanner. Every request counts against the
///     venue's scanner request budget; while the venue's ban pause lasts, requests fail with
///     <see cref="MarketScanErrorCodes.RequestsPaused" /> without being sent.
/// </summary>
public interface IMarketScanSource : IVenuePort
{
    /// <summary>
    ///     The most instruments one <see cref="Stream" /> may carry.
    /// </summary>
    int MaxSymbolsPerStream { get; }

    /// <summary>
    ///     The venue's read-only presets, tuned to its quote currency and fees (ADR 0059, section 4).
    /// </summary>
    IReadOnlyList<ScanPresetInternal> BuiltInPresets { get; }

    Task<IReadOnlyList<ScanInstrumentInternal>> Universe(CancellationToken cancellationToken);

    /// <summary>
    ///     The closed candles of [<paramref name="from" />, <paramref name="to" />) in ascending order; minutes the venue does not
    ///     report are missing, not filled.
    /// </summary>
    Task<IReadOnlyList<ScanCandle>> Candles(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ScanQuoteInternal>> Quotes(CancellationToken cancellationToken);

    /// <summary>
    ///     Closed and forming 1-minute candles of the given instruments over one connection; it throws or ends when the connection
    ///     is lost, and the caller connects again.
    /// </summary>
    IAsyncEnumerable<ScanCandleUpdateInternal> Stream(IReadOnlyList<InstrumentSymbol> symbols, CancellationToken cancellationToken);
}