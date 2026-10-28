using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotLeaseRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotLeaseRepository
{
    public async Task<BotLease?> GetForUpdate(BotId botId, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("A lease is locked only inside a transaction.");
        }

        var row = await connection.QuerySingleOrDefaultAsync<BotLeaseDb>(Command(BotLeaseQueries.GetForUpdate, new { botId = botId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task Add(BotLease lease, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotLeaseQueries.Insert, lease.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(lease);
    }

    public async Task Update(BotLease lease, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotLeaseQueries.Update, lease.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(lease);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}