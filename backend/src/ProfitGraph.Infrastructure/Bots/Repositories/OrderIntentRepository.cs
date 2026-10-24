using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class OrderIntentRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IOrderIntentRepository
{
    public async Task<bool> TryAdd(OrderIntent intent, CancellationToken cancellationToken)
    {
        int inserted = await connection.ExecuteAsync(Command(OrderIntentQueries.Insert, intent.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(intent);

        return inserted == 1;
    }

    public async Task<OrderIntent?> GetForUpdate(OrderIntentId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<OrderIntentDb>(Command(OrderIntentQueries.GetForUpdate, new { id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<OrderIntent>> ListByBot(BotId botId, CancellationToken cancellationToken)
    {
        IEnumerable<OrderIntentDb> rows =
            await connection.QueryAsync<OrderIntentDb>(Command(OrderIntentQueries.ListByBot, new { botId = botId.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task Update(OrderIntent intent, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(OrderIntentQueries.Update, intent.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(intent);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}