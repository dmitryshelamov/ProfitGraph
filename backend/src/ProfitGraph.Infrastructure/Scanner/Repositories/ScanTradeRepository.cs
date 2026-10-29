using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;
using ProfitGraph.Infrastructure.Scanner.Repositories.Converters;
using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner.Repositories;

internal sealed class ScanTradeRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IScanTradeRepository
{
    public Task Add(ScanTrade trade, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanTradeQueries.Add, trade.ToDb(), cancellationToken));
    }

    public Task Update(ScanTrade trade, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanTradeQueries.Update, trade.ToDb(), cancellationToken));
    }

    public Task<ScanTrade?> GetByBotForUpdate(BotId botId, CancellationToken cancellationToken)
    {
        return Single(ScanTradeQueries.GetByBotForUpdate, new { BotId = botId.Value }, cancellationToken);
    }

    public Task<ScanTrade?> Get(ScanTradeId id, CancellationToken cancellationToken)
    {
        return Single(ScanTradeQueries.Get, new { Id = id.Value }, cancellationToken);
    }

    public Task<ScanTrade?> GetForUpdate(ScanTradeId id, CancellationToken cancellationToken)
    {
        return Single(ScanTradeQueries.GetForUpdate, new { Id = id.Value }, cancellationToken);
    }

    public Task<IReadOnlyList<ScanTrade>> ListLive(int limit, CancellationToken cancellationToken)
    {
        return Many(ScanTradeQueries.ListLive, new { Limit = limit }, cancellationToken);
    }

    public Task<IReadOnlyList<ScanTrade>> ListUnreleased(int limit, CancellationToken cancellationToken)
    {
        return Many(ScanTradeQueries.ListUnreleased, new { Limit = limit }, cancellationToken);
    }

    public Task<IReadOnlyList<ScanTrade>> List(UserId ownerId, VenueId venue, string? symbol, int limit, CancellationToken cancellationToken)
    {
        return Many(
            ScanTradeQueries.List,
            new
            {
                OwnerId = ownerId.Value,
                Venue = venue.Value,
                Symbol = symbol,
                Limit = limit
            },
            cancellationToken);
    }

    private async Task<ScanTrade?> Single(string sql, object parameters, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanTradeDb>(Command(sql, parameters, cancellationToken));

        return row?.ToDomain();
    }

    private async Task<IReadOnlyList<ScanTrade>> Many(string sql, object parameters, CancellationToken cancellationToken)
    {
        IEnumerable<ScanTradeDb> rows = await connection.QueryAsync<ScanTradeDb>(Command(sql, parameters, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}