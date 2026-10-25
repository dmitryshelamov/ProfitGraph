using Dapper;

using Npgsql;

using ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;
using ProfitGraph.Venues.Steam.Protection;

namespace ProfitGraph.Venues.Steam.Persistence.Repositories;

internal sealed class SteamAccountProtectionRepository(NpgsqlDataSource dataSource) : ISteamAccountProtectionStore
{
    public async Task<(SteamProtectionReason Reason, DateTimeOffset OpenedAt)?> Get(Guid credentialId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SteamAccountProtectionDb>(
            new CommandDefinition(SteamAccountProtectionQueries.Get, new { credentialId }, cancellationToken: cancellationToken));

        return row is null ? null : (Enum.Parse<SteamProtectionReason>(row.Reason), row.OpenedAt.ToUniversalTime());
    }

    /// <summary>
    ///     Opens the protection; false when it was open already, and then its first reason stays.
    /// </summary>
    public async Task<bool> Open(Guid credentialId, SteamProtectionReason reason, DateTimeOffset openedAt, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(
            new CommandDefinition(
                SteamAccountProtectionQueries.Open,
                new { credentialId, reason = reason.ToString(), openedAt },
                cancellationToken: cancellationToken)) == 1;
    }

    public async Task Clear(Guid credentialId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(SteamAccountProtectionQueries.Clear, new { credentialId }, cancellationToken: cancellationToken));
    }
}