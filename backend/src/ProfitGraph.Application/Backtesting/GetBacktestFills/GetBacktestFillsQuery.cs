using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestFills;

/// <summary>
///     Fills after a ledger sequence.
/// </summary>
public sealed record GetBacktestFillsQuery(Guid UserId, Guid Id, long After, int Limit) : IQuery<BacktestPageInternal<BacktestLedgerEntryInternal>>
{
    public const int MaxLimit = 500;
}