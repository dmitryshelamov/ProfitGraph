using Dapper;

using Npgsql;

using ProfitGraph.Domain.Trading;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

/// <summary>
///     The halt is a single row that the migration creates.
/// </summary>
internal sealed class TradingHaltRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : ITradingHaltRepository
{
    public Task<TradingHalt> Get(CancellationToken cancellationToken)
    {
        return Read(TradingHaltQueries.Get, cancellationToken);
    }

    public Task<TradingHalt> GetForUpdate(CancellationToken cancellationToken)
    {
        return Read(TradingHaltQueries.GetForUpdate, cancellationToken);
    }

    public async Task Update(TradingHalt halt, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(TradingHaltQueries.Update, new TradingHaltDb { Halted = halt.IsHalted, UpdatedAt = halt.UpdatedAt }, cancellationToken));
        transactions.RegisterDomainEventClear(halt);
    }

    private async Task<TradingHalt> Read(string sql, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleAsync<TradingHaltDb>(Command(sql, parameters: null, cancellationToken));

        return TradingHalt.Restore(row.Halted, row.UpdatedAt);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}