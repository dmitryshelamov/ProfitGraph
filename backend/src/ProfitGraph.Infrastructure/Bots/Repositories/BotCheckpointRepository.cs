using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotCheckpointRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotCheckpointRepository
{
    public async Task<BotCheckpoint?> Get(BotId botId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<BotCheckpointDb>(Command(BotCheckpointQueries.Get, new { botId = botId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public Task Save(BotCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(BotCheckpointQueries.Upsert, checkpoint.ToDb(), cancellationToken));
    }

    public Task Delete(BotId botId, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(BotCheckpointQueries.Delete, new { botId = botId.Value }, cancellationToken));
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}