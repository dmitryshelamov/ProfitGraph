namespace ProfitGraph.Application.Common.Transactions;

/// <summary>
///     Runs application work inside one database transaction. Nested calls join the active transaction.
/// </summary>
public interface ITransactionExecutor
{
    /// <summary>
    ///     Refuses external I/O when a local or ambient transaction is already active.
    /// </summary>
    void EnsureNoTransaction();

    Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken);
}