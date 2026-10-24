using Npgsql;

namespace ProfitGraph.Tests.Integration;

/// <summary>
///     Creates one disposable database per test run on the test PostgreSQL and drops it afterwards, so concurrent runs
///     from other worktrees or agents never share state. Override the server with PROFITGRAPH_TEST_POSTGRES.
/// </summary>
[SetUpFixture]
public sealed class TestDatabase
{
    private const string DefaultServer = "Host=localhost;Port=5436;Username=postgres;Password=postgres;Database=postgres";

    private static string? CreatedConnectionString;

    public static string ConnectionString => CreatedConnectionString ?? throw new InvalidOperationException("The test database is not created yet.");

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PROFITGRAPH_TEST_POSTGRES") ?? DefaultServer);

        if (server.Port is 5433 or 5435)
        {
            throw new InvalidOperationException("Integration tests must not run against a development database.");
        }

        var name = $"profitgraph_test_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {name}";
        await command.ExecuteNonQueryAsync();
        CreatedConnectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = name }.ConnectionString;
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (CreatedConnectionString is null)
        {
            return;
        }

        var database = new NpgsqlConnectionStringBuilder(CreatedConnectionString);
        string name = database.Database!;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(CreatedConnectionString) { Database = "postgres" }.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS {name} WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}