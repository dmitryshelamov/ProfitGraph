using Dapper;

using Npgsql;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Ports;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;
using ProfitGraph.Infrastructure.Strategies.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Strategies.Repositories;

internal sealed class StrategyVersionReader(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IStrategyVersionReader
{
    public async Task<IReadOnlyList<StrategyVersionSummaryInternal>> List(Guid strategyId, CancellationToken cancellationToken)
    {
        IEnumerable<StrategyVersionDb> versions = await connection.QueryAsync<StrategyVersionDb>(
            StrategyVersionQueries.ListMetadata(strategyId, transactions.Current, cancellationToken));

        return [.. versions.Select(version => new StrategyVersionSummaryInternal(version.Id, version.Number, version.CreatedAt))];
    }
}