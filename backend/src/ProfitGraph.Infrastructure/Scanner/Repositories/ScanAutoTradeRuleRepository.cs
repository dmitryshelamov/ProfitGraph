using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;
using ProfitGraph.Infrastructure.Scanner.Repositories.Converters;
using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner.Repositories;

internal sealed class ScanAutoTradeRuleRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IScanAutoTradeRuleRepository
{
    public async Task<ScanAutoTradeRule?> Get(ScanPresetId presetId, UserId ownerId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanAutoTradeRuleDb>(
            Command(ScanAutoTradeRuleQueries.GetByPreset, new { PresetId = presetId.Value, OwnerId = ownerId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<ScanAutoTradeRule?> GetForUpdate(ScanPresetId presetId, UserId ownerId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanAutoTradeRuleDb>(
            Command(ScanAutoTradeRuleQueries.GetByPresetForUpdate, new { PresetId = presetId.Value, OwnerId = ownerId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<ScanAutoTradeRule?> GetForUpdate(ScanAutoTradeRuleId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanAutoTradeRuleDb>(
            Command(ScanAutoTradeRuleQueries.GetForUpdate, new { Id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<ScanAutoTradeRule>> ListOnForUpdate(VenueId venue, CancellationToken cancellationToken)
    {
        IEnumerable<ScanAutoTradeRuleDb> rows = await connection.QueryAsync<ScanAutoTradeRuleDb>(
            Command(ScanAutoTradeRuleQueries.ListOnForUpdate, new { Venue = venue.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public Task Add(ScanAutoTradeRule rule, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanAutoTradeRuleQueries.Add, rule.ToParameters(), cancellationToken));
    }

    public Task Update(ScanAutoTradeRule rule, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanAutoTradeRuleQueries.Update, rule.ToParameters(), cancellationToken));
    }

    public Task Delete(ScanAutoTradeRuleId id, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanAutoTradeRuleQueries.Delete, new { Id = id.Value }, cancellationToken));
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}