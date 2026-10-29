namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScanSnapshotQueries
{
    /// <summary>
    ///     Replaces the venue's rows with the snapshot's, one array per column, and signals <c>scan_snapshot</c> on commit.
    /// </summary>
    internal const string Replace = """
                                    DELETE FROM scan_snapshot_rows WHERE venue = @Venue;
                                    INSERT INTO scan_snapshot_rows (
                                        venue, symbol, computed_at, price, change_5m, change_15m, change_1h, change_4h, change_24h, turnover_1h,
                                        turnover_24h, volume_spike, natr_24h, efficiency_ratio_24h, distance_to_high_24h, distance_to_low_24h,
                                        distance_to_high_7d, distance_to_low_7d, spread, listing_age_days, stale, incomplete)
                                    SELECT @Venue, row.*
                                    FROM unnest(
                                        @Symbols, @ComputedAts, @Prices, @Change5m, @Change15m, @Change1h, @Change4h, @Change24h, @Turnover1h,
                                        @Turnover24h, @VolumeSpike, @Natr24h, @EfficiencyRatio24h, @DistanceToHigh24h, @DistanceToLow24h,
                                        @DistanceToHigh7d, @DistanceToLow7d, @Spread, @ListingAgeDays, @Stale, @Incomplete) AS row;
                                    SELECT pg_notify('scan_snapshot', @Signal::text);
                                    """;

    internal const string Newest = "SELECT max(computed_at) FROM scan_snapshot_rows";
}