using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     The shared cache of public candles (ADR 0010). Writes run in the caller's transaction.
/// </summary>
public interface ICandleRepository
{
    /// <summary>
    ///     Stores imported candles, replacing stored ones only where the values differ.
    /// </summary>
    Task Import(CandleSeries series, IReadOnlyList<Candle> candles, DateTimeOffset importedAt, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds candles that are not stored yet and returns how many were added; stored candles never change.
    /// </summary>
    Task<int> Append(CandleSeries series, IReadOnlyList<Candle> candles, string source, DateTimeOffset importedAt, CancellationToken cancellationToken);

    /// <summary>
    ///     Reads at most <paramref name="limit" /> candles of the half-open range [from, to), oldest first.
    /// </summary>
    Task<IReadOnlyList<Candle>> Get(CandleSeries series, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds market closures that are not stored yet (ADR 0023, TC1); stored closures never change.
    /// </summary>
    Task AppendClosures(CandleSeries series, IReadOnlyList<CandleRangeInternal> closures, string source, DateTimeOffset recordedAt, CancellationToken cancellationToken);

    /// <summary>
    ///     Reads the stored market closures that overlap [from, to), oldest first and clipped to the range.
    /// </summary>
    Task<IReadOnlyList<CandleRangeInternal>> GetClosures(CandleSeries series, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}