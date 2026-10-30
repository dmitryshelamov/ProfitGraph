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
                                        turnover_24h, volume_spike, volume_spike_5m, volume_spike_15m, natr_24h, efficiency_ratio_24h, distance_to_high_24h,
                                        distance_to_low_24h, distance_to_high_7d, distance_to_low_7d, breakout_4h, range_4h, resistance, support, distance_to_resistance, distance_to_support, reward_risk, spread,
                                        listing_age_days, stale, incomplete)
                                    SELECT @Venue, row.*
                                    FROM unnest(
                                        @Symbols, @ComputedAts, @Prices, @Change5m, @Change15m, @Change1h, @Change4h, @Change24h, @Turnover1h,
                                        @Turnover24h, @VolumeSpike, @VolumeSpike5m, @VolumeSpike15m, @Natr24h, @EfficiencyRatio24h, @DistanceToHigh24h,
                                        @DistanceToLow24h, @DistanceToHigh7d, @DistanceToLow7d, @Breakout4h, @Range4h, @Resistance, @Support, @DistanceToResistance, @DistanceToSupport, @RewardRisk, @Spread,
                                        @ListingAgeDays, @Stale, @Incomplete) AS row;
                                    SELECT pg_notify('scan_snapshot', @Signal::text);
                                    """;

    internal const string Newest = "SELECT max(computed_at) FROM scan_snapshot_rows";

    internal const string Get = """
                                SELECT venue AS Venue, symbol AS Symbol, computed_at AS ComputedAt, price AS Price, change_5m AS Change5m,
                                    change_15m AS Change15m, change_1h AS Change1h, change_4h AS Change4h, change_24h AS Change24h, turnover_1h AS Turnover1h,
                                    turnover_24h AS Turnover24h, volume_spike AS VolumeSpike, volume_spike_5m AS VolumeSpike5m,
                                    volume_spike_15m AS VolumeSpike15m, natr_24h AS Natr24h, efficiency_ratio_24h AS EfficiencyRatio24h,
                                    distance_to_high_24h AS DistanceToHigh24h, distance_to_low_24h AS DistanceToLow24h, distance_to_high_7d AS DistanceToHigh7d,
                                    distance_to_low_7d AS DistanceToLow7d, breakout_4h AS Breakout4h, range_4h AS Range4h, resistance AS Resistance, support AS Support,
                                    distance_to_resistance AS DistanceToResistance, distance_to_support AS DistanceToSupport, reward_risk AS RewardRisk, spread AS Spread, listing_age_days AS ListingAgeDays, stale AS Stale,
                                    incomplete AS Incomplete
                                FROM scan_snapshot_rows
                                WHERE venue = @Venue
                                ORDER BY symbol
                                """;
}