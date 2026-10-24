using Dapper;

using Npgsql;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories;

/// <summary>
///     Turns one finished run at a time into chart buckets, each in its own transaction.
/// </summary>
internal sealed class BacktestCompactor(NpgsqlConnection connection, ITransactionExecutor transactions, PostgresTransactionAccessor accessor)
{
    /// <summary>
    ///     Compacts the run whose job finished first before <paramref name="cutoff" />; false when there is none.
    /// </summary>
    public Task<bool> CompactNext(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                NpgsqlTransaction transaction = accessor.Current!;
                var runId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                    new CommandDefinition(BacktestCompactionQueries.LockNext, new { Cutoff = cutoff }, transaction, cancellationToken: cancellationToken));

                if (runId is null)
                {
                    return false;
                }

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        BacktestCompactionQueries.Compact,
                        new { RunId = runId.Value, BacktestChart.MaxPoints },
                        transaction,
                        cancellationToken: cancellationToken));

                return true;
            },
            cancellationToken);
    }
}