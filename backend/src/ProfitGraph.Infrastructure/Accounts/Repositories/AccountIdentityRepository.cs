using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Accounts.Identities;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Converters;
using ProfitGraph.Infrastructure.Accounts.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Accounts.Repositories;

internal sealed class AccountIdentityRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IAccountIdentityRepository
{
    public Task<AccountIdentity?> Get(ExchangeCredentialId credentialId, CancellationToken cancellationToken)
    {
        return Read(AccountIdentityQueries.Get, credentialId, cancellationToken);
    }

    public Task<AccountIdentity?> GetForUpdate(ExchangeCredentialId credentialId, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("An account identity is locked only inside a transaction.");
        }

        return Read(AccountIdentityQueries.GetForUpdate, credentialId, cancellationToken);
    }

    public async Task<IReadOnlyList<DueAccountIdentity>> ListDue(IReadOnlyList<VenueId> venues, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (venues.Count == 0)
        {
            return [];
        }

        IEnumerable<(Guid CredentialId, Guid OwnerId)> rows = await connection.QueryAsync<(Guid, Guid)>(
            Command(AccountIdentityQueries.ListDue, new { venues = venues.Select(venue => venue.Value).ToArray(), now }, cancellationToken));

        return [.. rows.Select(row => new DueAccountIdentity(new ExchangeCredentialId(row.CredentialId), new UserId(row.OwnerId)))];
    }

    public async Task Save(AccountIdentity identity, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(AccountIdentityQueries.Save, identity.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(identity);
    }

    private async Task<AccountIdentity?> Read(string sql, ExchangeCredentialId credentialId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<AccountIdentityDb>(Command(sql, new { credentialId = credentialId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}