using Dapper;

using Npgsql;

using ProfitGraph.Domain.MarketData;

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

    [Test]
    public async Task EverySymbolColumnHoldsTheLongestSymbol()
    {
        await using var factory = new ProfitGraphApiFactory();
        _ = factory.Server;
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        (string Table, int Length)[] columns = (await connection.QueryAsync<(string Table, int Length)>(
            "SELECT table_name, character_maximum_length FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'symbol'")).ToArray();

        columns.Select(column => column.Table)
            .ShouldBe(["bots", "candle_closures", "candles", "instrument_rules", "live_mark_prices", "scan_snapshot_rows", "scan_trades"], ignoreOrder: true);
        columns.ShouldAllBe(column => column.Length == InstrumentSymbol.MaxLength);
    }
}