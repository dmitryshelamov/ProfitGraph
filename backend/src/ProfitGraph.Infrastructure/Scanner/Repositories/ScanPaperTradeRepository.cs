using Dapper;

using Npgsql;

using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;
using ProfitGraph.Infrastructure.Scanner.Repositories.Converters;
using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner.Repositories;

internal sealed class ScanPaperTradeRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IScanPaperTradeRepository
{
    public Task Add(ScanPaperTrade trade, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanPaperTradeQueries.Add, trade.ToParameters(), cancellationToken));
    }

    public async Task<IReadOnlyList<ScanPaperTrade>> ListOpenForUpdate(VenueId venue, CancellationToken cancellationToken)
    {
        IEnumerable<ScanPaperTradeDb> rows = await connection.QueryAsync<ScanPaperTradeDb>(
            Command(ScanPaperTradeQueries.ListOpenForUpdate, new { Venue = venue.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<IReadOnlyList<ScanPaperTrade>> ListRecent(ScanAutoTradeRuleId ruleId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        IEnumerable<ScanPaperTradeDb> rows = await connection.QueryAsync<ScanPaperTradeDb>(
            Command(ScanPaperTradeQueries.ListRecent, new { RuleId = ruleId.Value, Since = since }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public Task Update(ScanPaperTrade trade, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanPaperTradeQueries.Update, trade.ToParameters(), cancellationToken));
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}