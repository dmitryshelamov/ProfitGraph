using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestRanking;

/// <summary>
///     The user's completed runs in one quote currency, ordered by a column (best full-period return first by default);
///     returns in different quote currencies are not comparable.
/// </summary>
public sealed record GetBacktestRankingQuery(
    Guid UserId,
    string Quote,
    string? Symbol,
    string? Timeframe,
    Guid? After,
    int Limit,
    BacktestRankingSort Sort = BacktestRankingSort.ReturnPercent,
    bool Ascending = false)
    : IQuery<BacktestSummaryPageInternal>
{
    public const int MaxLimit = 100;
}