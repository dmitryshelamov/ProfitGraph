using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     The stored scanner snapshot (ADR 0059): a venue's rows are replaced as a whole, other venues keep theirs, and the commit
///     signals <c>scan_snapshot</c>.
/// </summary>
[NonParallelizable]
public sealed class ScanSnapshotTests
{
    private static readonly DateTimeOffset ComputedAt = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 10, minute: 1, second: 5, TimeSpan.Zero);

    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task ASnapshotReplacesTheVenuesRowsAndSignalsOnCommit()
    {
        var dataSource = _api.Services.GetRequiredService<NpgsqlDataSource>();
        using var listening = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using IAsyncEnumerator<Guid> signals = PostgresSignals.Listen(dataSource, ["scan_snapshot"], listening.Token).GetAsyncEnumerator(listening.Token);
        ValueTask<bool> signalled = signals.MoveNextAsync();

        await Replace("other", Row("ETHUSDT", price: 2500m));
        (await signalled).ShouldBeTrue();

        await Replace("bybit", Row("BTCUSDT", price: 100m), Row("XRPUSDT", price: 0.5m));
        await Replace("bybit", Row("BTCUSDT", price: 101.25m, incomplete: true));

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        List<(string Venue, string Symbol, decimal? Price, decimal? Change1h, bool Stale, bool Incomplete, DateTime ComputedAt)> rows =
            (await connection.QueryAsync<(string Venue, string Symbol, decimal? Price, decimal? Change1h, bool Stale, bool Incomplete, DateTime ComputedAt)>(
                "SELECT venue, symbol, price, change_1h, stale, incomplete, computed_at FROM scan_snapshot_rows ORDER BY venue, symbol")).ToList();

        rows.Select(row => (row.Venue, row.Symbol, row.Price, row.Change1h, row.Stale, row.Incomplete))
            .ShouldBe(
            [
                ("bybit", "BTCUSDT", 101.25m, null, true, true),
                ("other", "ETHUSDT", 2500m, null, true, false)
            ]);
        new DateTimeOffset(DateTime.SpecifyKind(rows[0].ComputedAt, DateTimeKind.Utc)).ShouldBe(ComputedAt);
    }

    private async Task Replace(string venue, params ScanSnapshotRowInternal[] rows)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal(venue, ComputedAt, rows)));
    }

    private static ScanSnapshotRowInternal Row(string symbol, decimal price, bool incomplete = false)
    {
        return new ScanSnapshotRowInternal(symbol, new ScanMetrics { Price = price, Stale = true }, incomplete);
    }
}