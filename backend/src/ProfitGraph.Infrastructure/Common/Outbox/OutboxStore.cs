using Dapper;

using Npgsql;

using ProfitGraph.Application.Common.Outbox;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Common.Outbox;

internal sealed class OutboxStore(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IOutboxStore
{
    public Task<int> DeleteDelivered(DateTimeOffset before, int limit, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(new CommandDefinition(OutboxQueries.DeleteDelivered, new { before, limit }, transactions.Current, cancellationToken: cancellationToken));
    }
}
