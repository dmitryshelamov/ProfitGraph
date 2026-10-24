using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestCursor;

public sealed record GetBacktestCursorQuery(Guid UserId, Guid Id) : IQuery<BacktestCursorInternal>;