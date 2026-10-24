using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Application.Backtesting.History;

/// <summary>
///     Prepared history with the instrument rules captured for it and what a run or an optimization attaches.
/// </summary>
public sealed record LoadedBacktestHistory(PreparedHistory Prepared, InstrumentRulesDefinitionInternal Definition, BacktestHistoryInfo Info);