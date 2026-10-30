using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.PositionCloses;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotPositionCloseRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotPositionCloseRepository
{
    public Task<BotPositionClose?> Get(BotPositionCloseId id, CancellationToken cancellationToken)
    {
        return Read(BotPositionCloseQueries.Get, new { id = id.Value }, cancellationToken);
    }

    public Task<BotPositionClose?> GetForUpdate(BotPositionCloseId id, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("A position close is locked only inside a transaction.");
        }

        return Read(BotPositionCloseQueries.GetForUpdate, new { id = id.Value }, cancellationToken);
    }

    public Task<BotPositionClose?> GetActive(BotId botId, CancellationToken cancellationToken)
    {
        return Read(BotPositionCloseQueries.GetActive, new { botId = botId.Value }, cancellationToken);
    }

    public Task<BotPositionClose?> GetLatest(BotId botId, CancellationToken cancellationToken)
    {
        return Read(BotPositionCloseQueries.GetLatest, new { botId = botId.Value }, cancellationToken);
    }

    public async Task<bool> TryAdd(BotPositionClose close, CancellationToken cancellationToken)
    {
        int inserted = await connection.ExecuteAsync(Command(BotPositionCloseQueries.Insert, close.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(close);

        return inserted == 1;
    }

    public async Task Update(BotPositionClose close, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotPositionCloseQueries.Update, close.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(close);
    }

    private async Task<BotPositionClose?> Read(string sql, object parameters, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<BotPositionCloseDb>(Command(sql, parameters, cancellationToken));

        return row?.ToDomain();
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}