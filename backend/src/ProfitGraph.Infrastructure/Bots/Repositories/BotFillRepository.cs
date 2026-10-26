using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotFillRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotFillRepository
{
    public async Task<bool> TryAdd(BotFill fill, CancellationToken cancellationToken)
    {
        int inserted = await connection.ExecuteAsync(Command(BotFillQueries.Insert, fill.ToDb(), cancellationToken));

        if (inserted == 1)
        {
            transactions.RegisterDomainEventClear(fill);
        }
        else
        {
            fill.ClearDomainEvents();
        }

        return inserted == 1;
    }

    public async Task<IReadOnlyList<SequencedBotFill>> List(BotId botId, long? before, int limit, CancellationToken cancellationToken)
    {
        IEnumerable<BotFillDb> rows = await connection.QueryAsync<BotFillDb>(Command(BotFillQueries.List, new { botId = botId.Value, before, limit }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<IReadOnlyList<BotFill>> ListFilled(BotId botId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
    {
        IEnumerable<BotFillDb> rows = await connection.QueryAsync<BotFillDb>(
            Command(BotFillQueries.ListFilled, new { botId = botId.Value, from, to, limit }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain().Fill)];
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}