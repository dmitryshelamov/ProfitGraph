using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     Streams candle rows out of an uploaded CSV file; malformed input ends the stream with ApplicationValidationException.
/// </summary>
public interface ICsvCandleReader
{
    IAsyncEnumerable<CandleImportRowInternal> Read(Stream stream, CancellationToken cancellationToken);
}