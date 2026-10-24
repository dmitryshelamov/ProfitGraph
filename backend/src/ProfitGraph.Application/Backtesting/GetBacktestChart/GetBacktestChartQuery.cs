using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestChart;

/// <summary>
///     The run's price and equity chart in at most <see cref="BacktestChart.MaxPoints" /> points.
/// </summary>
public sealed record GetBacktestChartQuery(Guid UserId, Guid Id) : IQuery<BacktestChartInternal>;