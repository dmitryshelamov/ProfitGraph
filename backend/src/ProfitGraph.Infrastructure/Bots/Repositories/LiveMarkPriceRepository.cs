using Dapper;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class LiveMarkPriceRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : ILiveMarkPriceRepository
{
    public async Task<bool> Store(LiveMarkPrice price, CancellationToken cancellationToken)
    {
        var stored = await connection.ExecuteScalarAsync<long>(
            Command(
                LiveMarkPriceQueries.Store,
                new { id = Guid.NewGuid(), Venue = price.Venue.Value, Symbol = price.Symbol.Value, price.Price, price.ObservedAt },
                cancellationToken));

        return stored > 0;
    }

    public async Task<LiveMarkPrice?> Get(VenueId venue, InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<LiveMarkPriceDb>(
            Command(LiveMarkPriceQueries.Get, new { venue = venue.Value, symbol = symbol.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<LiveMarkPrice?> GetBySignal(Guid signal, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<LiveMarkPriceDb>(Command(LiveMarkPriceQueries.GetBySignal, new { id = signal }, cancellationToken));

        return row?.ToDomain();
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}