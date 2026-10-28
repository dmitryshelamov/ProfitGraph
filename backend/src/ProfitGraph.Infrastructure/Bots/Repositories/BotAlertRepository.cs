using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Alerts;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotAlertRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotAlertRepository
{
    public async Task<BotAlert?> GetForUpdate(BotAlertId id, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("An alert is locked only inside a transaction.");
        }

        var row = await connection.QuerySingleOrDefaultAsync<BotAlertDb>(Command(BotAlertQueries.GetForUpdate, new { id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<BotAlert>> List(UserId? ownerId, BotId? botId, bool unacknowledgedOnly, int limit, CancellationToken cancellationToken)
    {
        IEnumerable<BotAlertDb> rows = await connection.QueryAsync<BotAlertDb>(
            Command(BotAlertQueries.List, new { ownerId = ownerId?.Value, botId = botId?.Value, unacknowledgedOnly, limit }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task Add(BotAlert alert, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotAlertQueries.Insert, alert.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(alert);
    }

    public async Task Update(BotAlert alert, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotAlertQueries.Update, alert.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(alert);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}