using Dapper;

using Npgsql;

using ProfitGraph.Domain.Notifications;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Notifications.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Notifications.Repositories;

internal sealed class TelegramPollerLeaseRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : ITelegramPollerLeaseRepository
{
    public Task<long?> Acquire(Guid holderId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        return connection.QuerySingleOrDefaultAsync<long?>(Command(TelegramQueries.AcquireLease, new { holderId, now, expiresAt = now + duration }, cancellationToken));
    }

    public async Task<bool> Advance(Guid holderId, long updateOffset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return await connection.ExecuteAsync(Command(TelegramQueries.AdvanceOffset, new { holderId, updateOffset, now }, cancellationToken)) > 0;
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}