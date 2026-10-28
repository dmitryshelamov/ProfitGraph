using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestOrders;

/// <summary>
///     The latest state of each order after an order identifier.
/// </summary>
public sealed record GetBacktestOrdersQuery(Guid UserId, Guid Id, long After, int Limit) : IQuery<BacktestPageInternal<BacktestLedgerEntryInternal>>;