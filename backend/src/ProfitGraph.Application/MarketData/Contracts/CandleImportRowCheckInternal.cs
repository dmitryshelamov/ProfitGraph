using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     One row to check, with the import's timeframe and the single clock reading taken for the whole import.
/// </summary>
public sealed record CandleImportRowCheckInternal(CandleImportRowInternal Row, Timeframe Timeframe, DateTimeOffset Now);