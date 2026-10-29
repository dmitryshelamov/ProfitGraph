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

internal sealed class ScanPresetRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IScanPresetRepository
{
    public async Task<ScanPreset?> GetForUpdate(ScanPresetId id, UserId ownerId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanPresetDb>(
            Command(ScanPresetQueries.GetForUpdate, new { Id = id.Value, OwnerId = ownerId.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<ScanPreset>> List(UserId ownerId, VenueId venue, CancellationToken cancellationToken)
    {
        IEnumerable<ScanPresetDb> rows = await connection.QueryAsync<ScanPresetDb>(
            Command(ScanPresetQueries.List, new { OwnerId = ownerId.Value, Venue = venue.Value }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public Task<bool> NameTaken(UserId ownerId, VenueId venue, ScanPresetName name, ScanPresetId? except, CancellationToken cancellationToken)
    {
        return connection.ExecuteScalarAsync<bool>(
            Command(
                ScanPresetQueries.NameTaken,
                new { OwnerId = ownerId.Value, Venue = venue.Value, Name = name.Value, Except = except?.Value },
                cancellationToken));
    }

    public Task Add(ScanPreset preset, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanPresetQueries.Add, Parameters(preset), cancellationToken));
    }

    public Task Update(ScanPreset preset, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanPresetQueries.Update, Parameters(preset), cancellationToken));
    }

    public async Task<bool> Delete(ScanPresetId id, UserId ownerId, CancellationToken cancellationToken)
    {
        return await connection.ExecuteAsync(Command(ScanPresetQueries.Delete, new { Id = id.Value, OwnerId = ownerId.Value }, cancellationToken)) > 0;
    }

    private static object Parameters(ScanPreset preset)
    {
        return new
        {
            Id = preset.Id.Value,
            OwnerId = preset.OwnerId.Value,
            Venue = preset.Venue.Value,
            Name = preset.Name.Value,
            DefinitionJson = preset.ToDefinitionJson(),
            preset.CreatedAt,
            preset.UpdatedAt
        };
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}