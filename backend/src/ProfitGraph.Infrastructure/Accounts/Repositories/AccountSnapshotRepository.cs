using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Accounts.Snapshots;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Converters;
using ProfitGraph.Infrastructure.Accounts.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Accounts.Repositories;

internal sealed class AccountSnapshotRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IAccountSnapshotRepository
{
    public async Task<bool> TryAdd(AccountSnapshot snapshot, CancellationToken cancellationToken)
    {
        int inserted = await connection.ExecuteAsync(Command(AccountSnapshotQueries.Insert, snapshot.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(snapshot);

        return inserted == 1;
    }

    public async Task<IReadOnlyList<AccountSnapshot>> List(
        ExchangeCredentialId credentialId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken)
    {
        IEnumerable<AccountSnapshotDb> rows = await connection.QueryAsync<AccountSnapshotDb>(
            Command(AccountSnapshotQueries.List, new { credentialId = credentialId.Value, from, to, limit }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<AccountSnapshot?> Latest(ExchangeCredentialId credentialId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<AccountSnapshotDb>(
            Command(AccountSnapshotQueries.Latest, new { credentialId = credentialId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public Task<int> ThinToHourly(DateTimeOffset before, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(AccountSnapshotQueries.ThinToHourly, new { before }, cancellationToken));
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}