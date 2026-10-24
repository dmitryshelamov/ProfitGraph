using ProfitGraph.Application.Common.Transactions;

namespace ProfitGraph.Tests.Unit.Application.Backtesting;

/// <summary>
///     Runs the work directly and counts the transactions it would have opened.
/// </summary>
internal sealed class ImmediateTransactions : ITransactionExecutor
{
    public int Count { get; private set; }

    public void EnsureNoTransaction()
    {
    }

    public Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
    {
        Count++;

        return action();
    }
}