using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Api.Scanner.Converters;

internal static class ScanSnapshotResponseConverter
{
    public static ScanSnapshotResponse ToResponse(this ScanSnapshotInternal snapshot)
    {
        return new ScanSnapshotResponse(
            snapshot.Venue,
            snapshot.Rows.Count == 0 && snapshot.ComputedAt == DateTimeOffset.MinValue ? null : snapshot.ComputedAt,
            [.. snapshot.Rows.Select(ToResponse)]);
    }

    internal static ScanSnapshotRowResponse ToResponse(this ScanSnapshotRowInternal row)
    {
        ScanMetrics metrics = row.Metrics;

        return new ScanSnapshotRowResponse(
            row.Symbol,
            Text(metrics.Price),
            Text(metrics.Change5m),
            Text(metrics.Change15m),
            Text(metrics.Change1h),
            Text(metrics.Change4h),
            Text(metrics.Change24h),
            Text(metrics.Turnover1h),
            Text(metrics.Turnover24h),
            Text(metrics.VolumeSpike),
            Text(metrics.Natr24h),
            Text(metrics.EfficiencyRatio24h),
            Text(metrics.DistanceToHigh24h),
            Text(metrics.DistanceToLow24h),
            Text(metrics.DistanceToHigh7d),
            Text(metrics.DistanceToLow7d),
            Text(metrics.Spread),
            metrics.ListingAgeDays,
            metrics.Stale,
            row.Incomplete);
    }

    internal static string? Text(decimal? value)
    {
        return value is { } number ? ExactGraphNumberParser.Format(number) : null;
    }
}