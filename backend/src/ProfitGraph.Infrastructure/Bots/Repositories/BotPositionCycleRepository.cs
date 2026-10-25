using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotPositionCycleRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotPositionCycleRepository
{
    public async Task<BotPositionCycle?> GetOpenForUpdate(BotId botId, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("A position cycle is locked only inside a transaction.");
        }

        var row = await connection.QuerySingleOrDefaultAsync<BotPositionCycleDb>(
            Command(BotPositionCycleQueries.GetOpenForUpdate, new { botId = botId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<BotPositionCycle>> List(BotId botId, int limit, CancellationToken cancellationToken)
    {
        IEnumerable<BotPositionCycleDb> rows = await connection.QueryAsync<BotPositionCycleDb>(
            Command(BotPositionCycleQueries.List, new { botId = botId.Value, limit }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task Add(BotPositionCycle cycle, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotPositionCycleQueries.Insert, cycle.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(cycle);
    }

    public async Task Update(BotPositionCycle cycle, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotPositionCycleQueries.Update, cycle.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(cycle);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}