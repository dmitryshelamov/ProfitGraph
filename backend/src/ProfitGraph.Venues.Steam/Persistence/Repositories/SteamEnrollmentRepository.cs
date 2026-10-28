using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Venues.Steam.Enrollment;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

namespace ProfitGraph.Venues.Steam.Persistence.Repositories;

internal sealed class SteamEnrollmentRepository(NpgsqlDataSource dataSource) : ISteamEnrollmentStore
{
    public async Task Add(SteamPendingEnrollment enrollment, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(SteamEnrollmentQueries.Add, ToDb(enrollment), cancellationToken: cancellationToken));
    }

    public async Task<SteamPendingEnrollment?> Get(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SteamEnrollmentDb>(
            new CommandDefinition(SteamEnrollmentQueries.Get, new { id }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new SteamPendingEnrollment(
                row.Id,
                row.OwnerId,
                row.Step,
                new EncryptedSecret(row.KeyId, row.Nonce, row.Ciphertext, row.Tag),
                row.ExpiresAt.ToUniversalTime(),
                row.CreatedAt.ToUniversalTime(),
                row.UpdatedAt.ToUniversalTime());
    }

    public async Task<bool> Transition(SteamPendingEnrollment next, string expectedStep, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        SteamEnrollmentDb row = ToDb(next);

        return await connection.ExecuteAsync(
            new CommandDefinition(
                SteamEnrollmentQueries.Transition,
                new { row.Id, row.Step, row.KeyId, row.Nonce, row.Ciphertext, row.Tag, row.ExpiresAt, row.UpdatedAt, Expected = expectedStep },
                cancellationToken: cancellationToken)) == 1;
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(SteamEnrollmentQueries.Delete, new { id }, cancellationToken: cancellationToken));
    }

    private static SteamEnrollmentDb ToDb(SteamPendingEnrollment enrollment)
    {
        return new SteamEnrollmentDb
        {
            Id = enrollment.Id,
            OwnerId = enrollment.OwnerId,
            Step = enrollment.Step,
            KeyId = enrollment.Payload.KeyId,
            Nonce = enrollment.Payload.Nonce.ToArray(),
            Ciphertext = enrollment.Payload.Ciphertext.ToArray(),
            Tag = enrollment.Payload.Tag.ToArray(),
            ExpiresAt = enrollment.ExpiresAt,
            CreatedAt = enrollment.CreatedAt,
            UpdatedAt = enrollment.UpdatedAt
        };
    }
}