using Dapper;

using Npgsql;

using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;
using ProfitGraph.Infrastructure.Strategies.Repositories.Converters;
using ProfitGraph.Infrastructure.Strategies.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Strategies.Repositories;

/// <summary>
///     Сохраняет агрегат и неизменяемый снимок в транзакции Application-команды.
/// </summary>
internal sealed class StrategyRepository(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    StrategySnapshotDbConverter serializer) : IStrategyRepository
{
    public async Task Add(Strategy strategy, StrategyVersion version, CancellationToken cancellationToken)
    {
        RequireTransaction();
        await connection.ExecuteAsync(StrategyQueries.Add(strategy, transactions.Current, cancellationToken));
        transactions.RegisterDomainEventClear(strategy);
        await AddVersion(version, cancellationToken);
    }

    public async Task<Strategy?> Get(Guid id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<StrategyDb>(StrategyQueries.Get(id, transactions.Current, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<StrategyVersion?> GetVersion(Guid id, int number, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<StrategyVersionDb>(StrategyVersionQueries.Get(id, number, transactions.Current, cancellationToken));

        return row is null ? null : serializer.Deserialize(row);
    }

    public async Task<IReadOnlyList<StrategySummary>> List(Guid ownerId, int limit, CancellationToken cancellationToken)
    {
        IEnumerable<StrategySummaryDb> rows = await connection.QueryAsync<StrategySummaryDb>(
            StrategyQueries.List(ownerId, limit, transactions.Current, cancellationToken));

        return rows.Select(row => row.ToDomain()).ToArray();
    }

    public async Task<IReadOnlyList<StrategyVersion>> GetVersions(Guid id, CancellationToken cancellationToken)
    {
        IEnumerable<StrategyVersionDb> rows = await connection.QueryAsync<StrategyVersionDb>(StrategyVersionQueries.GetAll(id, transactions.Current, cancellationToken));

        return rows.Select(serializer.Deserialize).ToArray();
    }

    public async Task<bool> Save(Strategy strategy, StrategyVersion version, int expectedVersion, CancellationToken cancellationToken)
    {
        RequireTransaction();
        int affected = await connection.ExecuteAsync(StrategyQueries.Save(strategy, expectedVersion, transactions.Current, cancellationToken));

        if (affected == 0)
        {
            return false;
        }

        await AddVersion(version, cancellationToken);
        transactions.RegisterDomainEventClear(strategy);

        return true;
    }

    private Task AddVersion(StrategyVersion version, CancellationToken cancellationToken)
    {
        var row = new StrategyVersionDb
        {
            Id = version.Id,
            StrategyId = version.StrategyId,
            Number = version.Number,
            SnapshotJson = serializer.Serialize(version),
            CreatedAt = version.CreatedAt
        };

        return connection.ExecuteAsync(StrategyVersionQueries.Add(row, transactions.Current, cancellationToken));
    }

    private void RequireTransaction()
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("Strategy writes require an active transaction.");
        }
    }
}