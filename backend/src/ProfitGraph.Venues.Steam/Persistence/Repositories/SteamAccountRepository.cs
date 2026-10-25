using Dapper;

using Npgsql;

using ProfitGraph.Venues.Steam.Accounts;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

namespace ProfitGraph.Venues.Steam.Persistence.Repositories;

/// <summary>
///     Steam's own table (ADR 0022, C8), written in short statements of its own outside the core's transactions.
/// </summary>
internal sealed class SteamAccountRepository(NpgsqlDataSource dataSource) : ISteamAccountStore
{
    public async Task<SteamAccount?> Get(Guid credentialId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SteamAccountDb>(
            new CommandDefinition(SteamAccountQueries.Get, new { credentialId }, cancellationToken: cancellationToken));

        return row is null ? null : ToAccount(row);
    }

    public async Task<IReadOnlyList<SteamAccount>> Due(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<SteamAccountDb> rows = await connection.QueryAsync<SteamAccountDb>(
            new CommandDefinition(SteamAccountQueries.Due, new { now }, cancellationToken: cancellationToken));

        return [.. rows.Select(ToAccount)];
    }

    public async Task<bool> Record(
        Guid credentialId,
        SteamGuardStatus expected,
        SteamGuardStatus status,
        DateTimeOffset? nextCheckAt,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(
            new CommandDefinition(
                SteamAccountQueries.Record,
                new { credentialId, expected = expected.ToString(), status = status.ToString(), nextCheckAt, at },
                cancellationToken: cancellationToken)) == 1;
    }

    /// <summary>
    ///     Whether any stored account has one of the fingerprints, which a Steam ID has one of per configured master key.
    /// </summary>
    public async Task<bool> AnyFingerprint(IReadOnlyList<string> fingerprints, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(SteamAccountQueries.AnyFingerprint, new { fingerprints = fingerprints.ToArray() }, cancellationToken: cancellationToken));
    }

    /// <summary>
    ///     Stores a new account; false when another credential already has its fingerprint.
    /// </summary>
    public async Task<bool> Add(SteamAccount account, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = new SteamAccountDb
        {
            CredentialId = account.CredentialId,
            AccountFingerprint = account.AccountFingerprint,
            GuardStatus = account.GuardStatus.ToString(),
            NextCheckAt = account.NextCheckAt,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt
        };

        return await connection.ExecuteAsync(new CommandDefinition(SteamAccountQueries.Insert, row, cancellationToken: cancellationToken)) == 1;
    }

    private static SteamAccount ToAccount(SteamAccountDb row)
    {
        return new SteamAccount(
            row.CredentialId,
            row.AccountFingerprint,
            Enum.Parse<SteamGuardStatus>(row.GuardStatus),
            row.NextCheckAt?.ToUniversalTime(),
            row.CreatedAt.ToUniversalTime(),
            row.UpdatedAt.ToUniversalTime());
    }
}