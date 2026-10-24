using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Converters;
using ProfitGraph.Infrastructure.Accounts.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Accounts.Repositories;

internal sealed class ExchangeCredentialRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IExchangeCredentialRepository
{
    public async Task<ExchangeCredential?> Get(ExchangeCredentialId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ExchangeCredentialDb>(Command(ExchangeCredentialQueries.GetById, new { id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<ExchangeCredential>> List(UserId ownerId, CancellationToken cancellationToken)
    {
        IEnumerable<ExchangeCredentialDb> rows = await connection.QueryAsync<ExchangeCredentialDb>(
            Command(ExchangeCredentialQueries.ListByOwner, new { ownerId = ownerId.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task Add(ExchangeCredential credential, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(ExchangeCredentialQueries.Insert, credential.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(credential);
    }

    public async Task<bool> Delete(ExchangeCredentialId id, UserId ownerId, CancellationToken cancellationToken)
    {
        int deleted = await connection.ExecuteAsync(Command(ExchangeCredentialQueries.Delete, new { id = id.Value, ownerId = ownerId.Value }, cancellationToken));

        return deleted == 1;
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}