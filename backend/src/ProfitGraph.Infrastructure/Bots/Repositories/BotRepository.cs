using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;
using ProfitGraph.Infrastructure.Bots.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots.Repositories;

internal sealed class BotRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBotRepository
{
    public async Task<Bot?> Get(BotId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<BotDb>(Command(BotQueries.GetById, new { id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<Bot?> GetForUpdate(BotId id, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var row = await connection.QuerySingleOrDefaultAsync<BotDb>(Command(BotQueries.GetByIdForUpdate, new { id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Bot>> ListByOwner(UserId ownerId, CancellationToken cancellationToken)
    {
        IEnumerable<BotDb> rows = await connection.QueryAsync<BotDb>(Command(BotQueries.ListByOwner, new { ownerId = ownerId.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<IReadOnlyList<Bot>> ListDesiredRunningOnStrategy(StrategyId strategyId, CancellationToken cancellationToken)
    {
        IEnumerable<BotDb> rows = await connection.QueryAsync<BotDb>(
            Command(BotQueries.ListDesiredRunningOnStrategy, new { strategyId = strategyId.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<IReadOnlyList<Bot>> ListOnVenue(VenueId venue, CancellationToken cancellationToken)
    {
        IEnumerable<BotDb> rows = await connection.QueryAsync<BotDb>(Command(BotQueries.ListOnVenue, new { venue = venue.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<IReadOnlyList<LiveInstrument>> ListDesiredRunningInstruments(CancellationToken cancellationToken)
    {
        IEnumerable<(string Venue, string Symbol)> rows = await connection.QueryAsync<(string, string)>(
            Command(BotQueries.ListDesiredRunningInstruments, parameters: null, cancellationToken));

        return [.. rows.Select(row => new LiveInstrument(new VenueId(row.Venue), new InstrumentSymbol(row.Symbol)))];
    }

    public async Task<IReadOnlyList<BotId>> ListDesiredRunning(CancellationToken cancellationToken)
    {
        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(Command(BotQueries.ListDesiredRunning, parameters: null, cancellationToken));

        return [.. ids.Select(id => new BotId(id))];
    }

    public async Task<IReadOnlyList<BotId>> ListDegradedOnUnresolvedOrder(CancellationToken cancellationToken)
    {
        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(Command(BotQueries.ListDegradedOnUnresolvedOrder, parameters: null, cancellationToken));

        return [.. ids.Select(id => new BotId(id))];
    }

    public async Task<IReadOnlyList<BotId>> ListDesiredRunningOnSeries(CandleSeries series, CancellationToken cancellationToken)
    {
        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(
            Command(
                BotQueries.ListDesiredRunningOnSeries,
                new { venue = series.Venue.Value, symbol = series.Symbol.Value, timeframe = series.Timeframe.Key },
                cancellationToken));

        return [.. ids.Select(id => new BotId(id))];
    }

    public async Task<IReadOnlyList<BotId>> ListDesiredRunningOnCredential(ExchangeCredentialId credentialId, CancellationToken cancellationToken)
    {
        IEnumerable<Guid> ids =
            await connection.QueryAsync<Guid>(Command(BotQueries.ListDesiredRunningOnCredential, new { credentialId = credentialId.Value }, cancellationToken));

        return [.. ids.Select(id => new BotId(id))];
    }

    public Task<bool> AnyUsesCredential(ExchangeCredentialId credentialId, CancellationToken cancellationToken)
    {
        return connection.ExecuteScalarAsync<bool>(Command(BotQueries.AnyUsesCredential, new { credentialId = credentialId.Value }, cancellationToken));
    }

    public async Task Add(Bot bot, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Command(BotQueries.Insert, bot.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(bot);
    }

    public async Task Update(Bot bot, CancellationToken cancellationToken)
    {
        RequireTransaction();
        await connection.ExecuteAsync(Command(BotQueries.Update, bot.ToDb(), cancellationToken));
        transactions.RegisterDomainEventClear(bot);
    }

    private void RequireTransaction()
    {
        if (transactions.Current is null)
        {
            throw new InvalidOperationException("A bot is locked and written only inside a transaction.");
        }
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}