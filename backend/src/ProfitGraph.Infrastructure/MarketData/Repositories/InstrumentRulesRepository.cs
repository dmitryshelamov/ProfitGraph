using Dapper;

using Npgsql;

using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.MarketData.Repositories.Contracts;
using ProfitGraph.Infrastructure.MarketData.Repositories.Converters;
using ProfitGraph.Infrastructure.MarketData.Repositories.Queries;

namespace ProfitGraph.Infrastructure.MarketData.Repositories;

internal sealed class InstrumentRulesRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IInstrumentRulesRepository
{
    public async Task Add(InstrumentRulesSnapshotInternal snapshot, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("Instrument rules are captured inside a transaction.");
        }

        await connection.ExecuteAsync(
            new CommandDefinition(InstrumentRulesSnapshotQueries.Add, snapshot.ToDb(), transactions.Current, cancellationToken: cancellationToken));
    }

    public async Task<InstrumentRulesSnapshotInternal?> Get(Guid id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<InstrumentRulesSnapshotDb>(
            new CommandDefinition(InstrumentRulesSnapshotQueries.Get, new { Id = id }, transactions.Current, cancellationToken: cancellationToken));

        return row?.ToInternal();
    }
}