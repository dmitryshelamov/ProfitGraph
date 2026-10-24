using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Converters;
using ProfitGraph.Infrastructure.Accounts.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Accounts.Repositories;

internal sealed class UserRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IUserRepository
{
    public async Task<User?> GetById(UserId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<UserDb>(Command(UserQueries.GetById, new { id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<User?> GetByLogin(UserLogin login, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<UserDb>(Command(UserQueries.GetByLogin, new { login = login.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<int> CountActiveOwnersForUpdate(CancellationToken cancellationToken)
    {
        IEnumerable<Guid> owners = await connection.QueryAsync<Guid>(Command(UserQueries.LockActiveOwners, parameters: null, cancellationToken));

        return owners.Count();
    }

    public async Task<bool> TryAdd(User user, CancellationToken cancellationToken)
    {
        int inserted = await connection.ExecuteAsync(Command(UserQueries.Insert, user.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(user);

        return inserted == 1;
    }

    public async Task<bool> TryAddFirst(User user, CancellationToken cancellationToken)
    {
        int inserted = await connection.ExecuteAsync(Command(UserQueries.InsertFirst, user.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(user);

        return inserted == 1;
    }

    public async Task Update(User user, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(UserQueries.Update, user.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(user);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}