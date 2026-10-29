using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestRanking;

/// <summary>
///     The user's finished runs that pass <paramref name="Filter" />, ordered by a column (best full-period return first by
///     default). Completed runs are listed in one quote currency, because returns in different quote currencies are not
///     comparable; failed and cancelled runs have no result and are ordered by start time only.
/// </summary>
public sealed record GetBacktestRankingQuery(
    Guid UserId,
    BacktestRankingFilterInternal Filter,
    Guid? After,
    int Limit,
    BacktestRankingSort Sort = BacktestRankingSort.ReturnPercent,
    bool Ascending = false)
    : IQuery<BacktestSummaryPageInternal>;