using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;
using ProfitGraph.Infrastructure.Scanner.Repositories.Converters;
using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner.Repositories;

internal sealed class ScanAlertWatchRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IScanAlertWatchRepository
{
    public async Task<ScanAlertWatch?> GetForUpdate(ScanPresetId presetId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanAlertWatchDb>(
            Command(ScanAlertWatchQueries.GetForUpdate, new { PresetId = presetId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<ScanAlertWatch>> ListForUpdate(VenueId venue, CancellationToken cancellationToken)
    {
        IEnumerable<ScanAlertWatchDb> rows = await connection.QueryAsync<ScanAlertWatchDb>(
            Command(ScanAlertWatchQueries.ListForUpdate, new { Venue = venue.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<IReadOnlySet<ScanPresetId>> ListPresets(UserId ownerId, CancellationToken cancellationToken)
    {
        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(Command(ScanAlertWatchQueries.ListPresets, new { OwnerId = ownerId.Value }, cancellationToken));

        return ids.Select(id => new ScanPresetId(id)).ToHashSet();
    }

    public Task Add(ScanAlertWatch watch, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanAlertWatchQueries.Add, Parameters(watch), cancellationToken));
    }

    public Task Update(ScanAlertWatch watch, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanAlertWatchQueries.Update, Parameters(watch), cancellationToken));
    }

    public Task Delete(ScanPresetId presetId, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanAlertWatchQueries.Delete, new { PresetId = presetId.Value }, cancellationToken));
    }

    private static object Parameters(ScanAlertWatch watch)
    {
        return new
        {
            PresetId = watch.PresetId.Value,
            OwnerId = watch.OwnerId.Value,
            watch.EnabledAt,
            watch.CheckedAt,
            Pairs = watch.ToPairsJson()
        };
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}