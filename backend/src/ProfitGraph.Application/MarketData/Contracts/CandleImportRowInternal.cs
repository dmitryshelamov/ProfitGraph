using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     A parsed CSV row with its line number in the file, for error messages.
/// </summary>
public sealed record CandleImportRowInternal(Candle Candle, int LineNumber);