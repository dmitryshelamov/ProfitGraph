using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.BalanceStatuses;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Converters;
using ProfitGraph.Infrastructure.Accounts.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Accounts.Repositories;

internal sealed class AccountBalanceStatusRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IAccountBalanceStatusRepository
{
    public Task<AccountBalanceStatus?> Get(ExchangeCredentialId credentialId, CancellationToken cancellationToken)
    {
        return Read(AccountBalanceStatusQueries.Get, credentialId, cancellationToken);
    }

    public Task<AccountBalanceStatus?> GetForUpdate(ExchangeCredentialId credentialId, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("A balance status is locked only inside a transaction.");
        }

        return Read(AccountBalanceStatusQueries.GetForUpdate, credentialId, cancellationToken);
    }

    public async Task<IReadOnlyList<DueAccountBalance>> ListDue(DateTimeOffset now, CancellationToken cancellationToken)
    {
        IEnumerable<(Guid CredentialId, Guid OwnerId)> rows = await connection.QueryAsync<(Guid, Guid)>(
            Command(AccountBalanceStatusQueries.ListDue, new { now }, cancellationToken));

        return [.. rows.Select(row => new DueAccountBalance(new ExchangeCredentialId(row.CredentialId), new UserId(row.OwnerId)))];
    }

    public async Task Save(AccountBalanceStatus status, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(AccountBalanceStatusQueries.Save, status.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(status);
    }

    private async Task<AccountBalanceStatus?> Read(string sql, ExchangeCredentialId credentialId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<AccountBalanceStatusDb>(Command(sql, new { credentialId = credentialId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}