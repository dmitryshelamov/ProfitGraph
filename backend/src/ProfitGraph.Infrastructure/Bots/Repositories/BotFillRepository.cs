using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Fills;
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

    public async Task<IReadOnlyList<decimal>> QuoteFees(BotId botId, IReadOnlyList<BotFillRange> ranges, CancellationToken cancellationToken)
    {
        if (ranges.Count == 0)
        {
            return [];
        }

        IEnumerable<decimal> fees = await connection.QueryAsync<decimal>(
            Command(
                BotFillQueries.QuoteFees,
                new { botId = botId.Value, froms = ranges.Select(range => range.From).ToArray(), tos = ranges.Select(range => range.To).ToArray() },
                cancellationToken));

        return [.. fees];
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}