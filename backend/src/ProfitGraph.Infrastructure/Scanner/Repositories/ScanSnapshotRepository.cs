using Dapper;

using Npgsql;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;
using ProfitGraph.Infrastructure.Scanner.Repositories.Converters;
using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner.Repositories;

internal sealed class ScanSnapshotRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IScanSnapshotRepository
{
    public async Task Replace(ScanSnapshotInternal snapshot, CancellationToken cancellationToken)
    {
        IReadOnlyList<ScanSnapshotRowInternal> rows = snapshot.Rows;

        var parameters = new
        {
            snapshot.Venue,
            Signal = Guid.NewGuid(),
            Symbols = Column(rows, row => row.Symbol),
            ComputedAts = Column(rows, _ => snapshot.ComputedAt),
            Prices = Metric(rows, metrics => metrics.Price),
            Change5m = Metric(rows, metrics => metrics.Change5m),
            Change15m = Metric(rows, metrics => metrics.Change15m),
            Change1h = Metric(rows, metrics => metrics.Change1h),
            Change4h = Metric(rows, metrics => metrics.Change4h),
            Change24h = Metric(rows, metrics => metrics.Change24h),
            Turnover1h = Metric(rows, metrics => metrics.Turnover1h),
            Turnover24h = Metric(rows, metrics => metrics.Turnover24h),
            VolumeSpike = Metric(rows, metrics => metrics.VolumeSpike),
            VolumeSpike5m = Metric(rows, metrics => metrics.VolumeSpike5m),
            VolumeSpike15m = Metric(rows, metrics => metrics.VolumeSpike15m),
            Natr24h = Metric(rows, metrics => metrics.Natr24h),
            EfficiencyRatio24h = Metric(rows, metrics => metrics.EfficiencyRatio24h),
            DistanceToHigh24h = Metric(rows, metrics => metrics.DistanceToHigh24h),
            DistanceToLow24h = Metric(rows, metrics => metrics.DistanceToLow24h),
            DistanceToHigh7d = Metric(rows, metrics => metrics.DistanceToHigh7d),
            DistanceToLow7d = Metric(rows, metrics => metrics.DistanceToLow7d),
            Breakout4h = Metric(rows, metrics => metrics.Breakout4h),
            Range4h = Metric(rows, metrics => metrics.Range4h),
            Resistance = Metric(rows, metrics => metrics.Resistance),
            Support = Metric(rows, metrics => metrics.Support),
            DistanceToResistance = Metric(rows, metrics => metrics.DistanceToResistance),
            DistanceToSupport = Metric(rows, metrics => metrics.DistanceToSupport),
            RewardRisk = Metric(rows, metrics => metrics.RewardRisk),
            Spread = Metric(rows, metrics => metrics.Spread),
            ListingAgeDays = Column(rows, row => row.Metrics.ListingAgeDays),
            Stale = Column(rows, row => row.Metrics.Stale),
            Incomplete = Column(rows, row => row.Incomplete)
        };

        await connection.ExecuteAsync(new CommandDefinition(ScanSnapshotQueries.Replace, parameters, transactions.Current, cancellationToken: cancellationToken));
    }

    public async Task<ScanSnapshotInternal?> Get(string venue, CancellationToken cancellationToken)
    {
        List<ScanSnapshotRowDb> rows =
        [
            .. await connection.QueryAsync<ScanSnapshotRowDb>(
                new CommandDefinition(ScanSnapshotQueries.Get, new { Venue = venue }, transactions.Current, cancellationToken: cancellationToken))
        ];

        return rows.Count == 0 ? null : new ScanSnapshotInternal(venue, rows.Max(row => row.ComputedAt), [.. rows.Select(row => row.ToInternal())]);
    }

    private static T[] Column<T>(IReadOnlyList<ScanSnapshotRowInternal> rows, Func<ScanSnapshotRowInternal, T> value)
    {
        return [.. rows.Select(value)];
    }

    private static decimal?[] Metric(IReadOnlyList<ScanSnapshotRowInternal> rows, Func<ScanMetrics, decimal?> value)
    {
        return [.. rows.Select(row => value(row.Metrics))];
    }
}