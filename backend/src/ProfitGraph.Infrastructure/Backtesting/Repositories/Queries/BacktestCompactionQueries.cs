namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Queries;

/// <summary>
///     Compaction of finished runs into chart buckets (Profit Fox Designer ADR 0046), one run per transaction.
/// </summary>
internal static class BacktestCompactionQueries
{
    /// <summary>
    ///     Locks the run whose job finished first before the cutoff and is not compacted yet; another compactor skips it.
    /// </summary>
    public const string LockNext =
        """
        SELECT r.id
        FROM backtest_runs r
        JOIN background_jobs j ON j.id = r.id
        WHERE r.chart_bucket_size IS NULL AND j.status IN ('Completed', 'Failed', 'Cancelled') AND j.finished_at <= @Cutoff
        ORDER BY j.finished_at, r.id
        LIMIT 1
        FOR UPDATE OF r SKIP LOCKED;
        """;

    /// <summary>
    ///     Buckets the candle observations of a finished run once and keeps only the last observation.
    /// </summary>
    public const string Compact =
        $"""
         WITH size AS (
             SELECT {BacktestChartQueries.BucketSize} AS value
             FROM backtest_runs
             WHERE id = @RunId AND chart_bucket_size IS NULL
         ), {BacktestChartQueries.Aggregate}, inserted AS (
             INSERT INTO backtest_chart_buckets (
                 run_id, bucket_index, first_sequence, last_sequence, first_open_time, last_open_time, open, high, low, close, volume, equity,
                 min_equity, max_equity, total_pnl, buy_fills, sell_fills)
             SELECT @RunId, bucket_index, first_sequence, last_sequence, first_open_time, last_open_time, open, high, low, close, volume, equity,
                 min_equity, max_equity, total_pnl, buy_fills, sell_fills
             FROM buckets
         ), deleted AS (
             DELETE FROM backtest_candles
             WHERE run_id = @RunId AND EXISTS (SELECT 1 FROM size)
                 AND sequence < (SELECT max(sequence) FROM backtest_candles WHERE run_id = @RunId)
         )
         UPDATE backtest_runs SET chart_bucket_size = size.value
         FROM size
         WHERE id = @RunId;
         """;
}