using Dapper;

using Npgsql;

namespace ProfitGraph.Tests.Integration.Common;

public sealed class MigrationRunnerTests
{
    [Test]
    public async Task StartupAppliesMigrationsAndRecordsThem()
    {
        await using var factory = new ProfitGraphApiFactory();
        _ = factory.Server;
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        (await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.\"VersionInfo\"') IS NOT NULL")).ShouldBeTrue();
        (await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.users') IS NOT NULL")).ShouldBeTrue();
    }
}