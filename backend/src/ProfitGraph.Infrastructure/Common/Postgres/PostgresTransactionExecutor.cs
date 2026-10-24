using System.Data;
using System.Transactions;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Infrastructure.Common.Outbox;

namespace ProfitGraph.Infrastructure.Common.Postgres;

internal sealed class PostgresTransactionExecutor(NpgsqlConnection connection, PostgresTransactionAccessor transactionAccessor, OutboxWriter outbox)
    : ITransactionExecutor
{
    public void EnsureNoTransaction()
    {
        if (transactionAccessor.Current is not null || Transaction.Current is not null)
        {
            throw new InvalidOperationException("External I/O cannot run inside an active database transaction.");
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (transactionAccessor.Current is not null)
        {
            return await action();
        }

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            transactionAccessor.Current = transaction;

            try
            {
                TResult result = await action();
                await outbox.Write(transactionAccessor.RegisteredAggregates, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionAccessor.ClearRegisteredDomainEvents();

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                transactionAccessor.DiscardRegisteredDomainEventClears();

                throw;
            }
            finally
            {
                if (ReferenceEquals(transactionAccessor.Current, transaction))
                {
                    transactionAccessor.Current = null;
                }
            }
        }
        finally
        {
            if (connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync();
            }
        }
    }
}