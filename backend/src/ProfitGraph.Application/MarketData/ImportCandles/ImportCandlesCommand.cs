using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.ImportCandles;

/// <summary>
///     Imports a streamed CSV into the shared candle cache in one transaction: an invalid file stores nothing.
/// </summary>
public sealed record ImportCandlesCommand(string Venue, string Symbol, string Timeframe, IAsyncEnumerable<CandleImportRowInternal> Candles)
    : ICommand<CandleImportInternal>;