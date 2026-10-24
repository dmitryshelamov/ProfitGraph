using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestRun;

public sealed record GetBacktestRunQuery(Guid UserId, Guid Id) : IQuery<BacktestRunInternal>;