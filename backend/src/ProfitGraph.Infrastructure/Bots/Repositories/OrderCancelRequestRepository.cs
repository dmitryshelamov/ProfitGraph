using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class OrderCancelRequestRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IOrderCancelRequestRepository
{
    public Task<OrderCancelRequest?> Get(OrderCancelRequestId id, CancellationToken cancellationToken)
    {
        return Read(OrderCancelRequestQueries.Get, new { id = id.Value }, cancellationToken);
    }

    public Task<OrderCancelRequest?> GetForUpdate(OrderCancelRequestId id, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("An order cancel is locked only inside a transaction.");
        }

        return Read(OrderCancelRequestQueries.GetForUpdate, new { id = id.Value }, cancellationToken);
    }

    public Task<OrderCancelRequest?> GetPending(BotId botId, CancellationToken cancellationToken)
    {
        return Read(OrderCancelRequestQueries.GetPending, new { botId = botId.Value }, cancellationToken);
    }

    public async Task<bool> TryAdd(OrderCancelRequest request, CancellationToken cancellationToken)
    {
        int inserted = await connection.ExecuteAsync(Command(OrderCancelRequestQueries.Insert, request.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(request);

        return inserted == 1;
    }

    public async Task Update(OrderCancelRequest request, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(OrderCancelRequestQueries.Update, request.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(request);
    }

    private async Task<OrderCancelRequest?> Read(string sql, object parameters, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<OrderCancelRequestDb>(Command(sql, parameters, cancellationToken));

        return row?.ToDomain();
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}