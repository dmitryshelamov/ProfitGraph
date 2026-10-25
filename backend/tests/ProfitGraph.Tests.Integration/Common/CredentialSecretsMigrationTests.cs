using Dapper;

using FluentMigrator.Runner;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Tests.Integration.Common;

/// <summary>
///     Migration 202610250020 moves stored key and secret columns into <c>secrets</c> byte for byte, never decrypting them
///     (ADR 0022, C6). It runs on a database of its own, created and dropped by the test.
/// </summary>
public sealed class CredentialSecretsMigrationTests
{
    private const long BeforeSecretsByField = 202610250019;

    [Test]
    public async Task KeyAndSecretColumnsMoveIntoSecretsUnchanged()
    {
        var database = $"profitgraph_migration_{Guid.NewGuid():N}";
        var server = new NpgsqlConnectionStringBuilder(TestDatabase.ConnectionString);
        string connectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = database }.ConnectionString;
        await Execute(server.ConnectionString, $"CREATE DATABASE {database}");

        try
        {
            byte[] keyCipher = [.. Enumerable.Range(start: 0, count: 120).Select(index => (byte)index)];
            byte[] secretCipher = [.. Enumerable.Range(start: 0, count: 90).Select(index => (byte)(255 - index))];
            byte[] nonce = [.. Enumerable.Repeat((byte)7, count: 12)];
            byte[] tag = [.. Enumerable.Repeat((byte)9, count: 16)];
            var user = Guid.NewGuid();
            var credential = Guid.NewGuid();

            Migrate(connectionString, BeforeSecretsByField);

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.ExecuteAsync(
                    sql: """
                         INSERT INTO users (id, login, password_hash, role, status, created_at, updated_at)
                         VALUES (@user, 'migration-owner', 'not-a-real-hash', 'User', 'Active', now(), now());
                         INSERT INTO exchange_credentials (
                             id, user_id, venue, label, api_key_last4, api_key_key_id, api_key_nonce, api_key_ciphertext, api_key_tag,
                             api_secret_key_id, api_secret_nonce, api_secret_ciphertext, api_secret_tag, created_at)
                         VALUES (@credential, @user, 'bybit', 'invented', 'abcd', 'key-a', @nonce, @keyCipher, @tag, 'key-b', @nonce, @secretCipher, @tag, now());
                         """,
                    new { user, credential, nonce, keyCipher, secretCipher, tag });
            }

            Migrate(connectionString, version: null);
            await using var migrated = new NpgsqlConnection(connectionString);
            var row = await migrated.QuerySingleAsync<(string KeyId, byte[] Key, byte[] KeyNonce, byte[] KeyTag, string SecretKeyId, byte[] Secret)>(
                sql: """
                     SELECT secrets -> 'apiKey' ->> 'keyId', decode(secrets -> 'apiKey' ->> 'ciphertext', 'base64'),
                            decode(secrets -> 'apiKey' ->> 'nonce', 'base64'), decode(secrets -> 'apiKey' ->> 'tag', 'base64'),
                            secrets -> 'apiSecret' ->> 'keyId', decode(secrets -> 'apiSecret' ->> 'ciphertext', 'base64')
                     FROM exchange_credentials WHERE id = @credential
                     """,
                new { credential });

            row.ShouldSatisfyAllConditions(
                () => row.KeyId.ShouldBe("key-a"),
                () => row.Key.ShouldBe(keyCipher),
                () => row.KeyNonce.ShouldBe(nonce),
                () => row.KeyTag.ShouldBe(tag),
                () => row.SecretKeyId.ShouldBe("key-b"),
                () => row.Secret.ShouldBe(secretCipher));
            (await migrated.ExecuteScalarAsync<bool>("SELECT secrets ->> 'apiKey' NOT LIKE '%\n%' FROM exchange_credentials")).ShouldBeTrue();
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Execute(server.ConnectionString, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static void Migrate(string connectionString, long? version)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(PostgresRegistration).Assembly)
                .For.Migrations())
            .BuildServiceProvider();
        var runner = provider.GetRequiredService<IMigrationRunner>();

        if (version is { } target)
        {
            runner.MigrateUp(target);
        }
        else
        {
            runner.MigrateUp();
        }
    }

    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync(sql);
    }
}