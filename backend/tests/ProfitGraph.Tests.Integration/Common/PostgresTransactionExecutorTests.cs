using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Tests.Integration.Common;

public sealed class PostgresTransactionExecutorTests
{
    private ProfitGraphApiFactory _factory = null!;

    [OneTimeSetUp]
    public async Task CreateProbeTable()
    {
        _factory = new ProfitGraphApiFactory();
        _ = _factory.Server;
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS transaction_probe (id integer PRIMARY KEY)");
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync("DROP TABLE IF EXISTS transaction_probe");
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task CommittedWorkIsVisibleAfterward()
    {
        await RunInTransaction(id: 1, fail: false);

        (await Exists(id: 1)).ShouldBeTrue();
    }

    [Test]
    public async Task FailedWorkIsRolledBack()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => RunInTransaction(id: 2, fail: true));

        (await Exists(id: 2)).ShouldBeFalse();
    }

    [Test]
    public async Task NestedExecutionJoinsTheActiveTransaction()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        var connection = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
        var accessor = scope.ServiceProvider.GetRequiredService<PostgresTransactionAccessor>();

        await Should.ThrowAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            async () =>
            {
                await executor.ExecuteAsync(
                    async () => await connection.ExecuteAsync("INSERT INTO transaction_probe (id) VALUES (3)", transaction: accessor.Current),
                    CancellationToken.None);
                executor.EnsureNoTransaction();

                return true;
            },
            CancellationToken.None));

        (await Exists(id: 3)).ShouldBeFalse();
    }

    private async Task RunInTransaction(int id, bool fail)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        var connection = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
        var accessor = scope.ServiceProvider.GetRequiredService<PostgresTransactionAccessor>();

        await executor.ExecuteAsync(
            async () =>
            {
                await connection.ExecuteAsync("INSERT INTO transaction_probe (id) VALUES (@id)", new { id }, accessor.Current);

                return fail ? throw new InvalidOperationException("Work failed after the insert.") : true;
            },
            CancellationToken.None);
    }

    private static async Task<bool> Exists(int id)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return await connection.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM transaction_probe WHERE id = @id)", new { id });
    }
}