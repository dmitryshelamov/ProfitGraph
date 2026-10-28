using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetStrategyBacktests;

/// <summary>
///     Runs of one of the user's strategies, newest first; <paramref name="After" /> is the last run of the previous page.
/// </summary>
public sealed record GetStrategyBacktestsQuery(Guid UserId, Guid StrategyId, Guid? After, int Limit) : IQuery<BacktestSummaryPageInternal>;