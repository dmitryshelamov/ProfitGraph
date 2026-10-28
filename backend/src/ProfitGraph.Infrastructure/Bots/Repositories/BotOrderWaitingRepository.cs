using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotOrderWaitingRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotOrderWaitingRepository
{
    public async Task<IReadOnlyList<BotOrderWaiting>> List(BotId botId, CancellationToken cancellationToken)
    {
        IEnumerable<BotOrderWaitingDb> rows =
            await connection.QueryAsync<BotOrderWaitingDb>(Command(BotOrderWaitingQueries.List, new { botId = botId.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<BotOrderWaiting?> GetForUpdate(BotId botId, long engineOrderId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<BotOrderWaitingDb>(
            Command(BotOrderWaitingQueries.GetForUpdate, new { botId = botId.Value, engineOrderId }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task Save(BotOrderWaiting waiting, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotOrderWaitingQueries.Upsert, waiting.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(waiting);
    }

    public async Task Delete(BotOrderWaiting waiting, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            Command(BotOrderWaitingQueries.Delete, new { botId = waiting.BotId.Value, engineOrderId = waiting.EngineOrderId }, cancellationToken));
        transactions.RegisterDomainEventClear(waiting);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}