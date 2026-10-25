using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotFeedRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotFeedRepository
{
    public async Task Add(BotFeedEntry entry, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotFeedQueries.Insert, entry.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(entry);
    }

    public async Task<IReadOnlyList<SequencedBotFeedEntry>> List(BotId botId, long? before, int limit, CancellationToken cancellationToken)
    {
        IEnumerable<BotFeedEntryDb> rows =
            await connection.QueryAsync<BotFeedEntryDb>(Command(BotFeedQueries.List, new { botId = botId.Value, before, limit }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public Task<int> DeleteOlderThan(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(BotFeedQueries.DeleteOlderThan, new { cutoff }, cancellationToken));
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}