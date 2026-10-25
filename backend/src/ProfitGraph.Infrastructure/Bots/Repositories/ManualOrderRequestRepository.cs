using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class ManualOrderRequestRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IManualOrderRequestRepository
{
    public Task<ManualOrderRequest?> Get(ManualOrderRequestId id, CancellationToken cancellationToken)
    {
        return Read(ManualOrderRequestQueries.Get, new { id = id.Value }, cancellationToken);
    }

    public Task<ManualOrderRequest?> GetForUpdate(ManualOrderRequestId id, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("A manual order is locked only inside a transaction.");
        }

        return Read(ManualOrderRequestQueries.GetForUpdate, new { id = id.Value }, cancellationToken);
    }

    public Task<ManualOrderRequest?> GetPending(BotId botId, CancellationToken cancellationToken)
    {
        return Read(ManualOrderRequestQueries.GetPending, new { botId = botId.Value }, cancellationToken);
    }

    public async Task<bool> TryAdd(ManualOrderRequest request, CancellationToken cancellationToken)
    {
        int inserted = await connection.ExecuteAsync(Command(ManualOrderRequestQueries.Insert, request.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(request);

        return inserted == 1;
    }

    public async Task Update(ManualOrderRequest request, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(ManualOrderRequestQueries.Update, request.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(request);
    }

    private async Task<ManualOrderRequest?> Read(string sql, object parameters, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ManualOrderRequestDb>(Command(sql, parameters, cancellationToken));

        return row?.ToDomain();
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}