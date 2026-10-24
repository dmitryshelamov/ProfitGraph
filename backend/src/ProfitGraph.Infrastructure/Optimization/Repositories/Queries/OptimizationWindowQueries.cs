namespace ProfitGraph.Infrastructure.Optimization.Repositories.Queries;

internal static class OptimizationWindowQueries
{
    /// <summary>
    ///     A window that a retried attempt finishes again keeps what it first stored.
    /// </summary>
    public const string Write =
        """
        INSERT INTO optimization_windows (optimization_id, window_index, train_start, train_count, test_start, test_count, train_from, train_to,
            test_from, test_to, winner_trial_index, parameters, train_metrics, test_metrics, status, failure_code, equity)
        VALUES (@OptimizationId, @WindowIndex, @TrainStart, @TrainCount, @TestStart, @TestCount, @TrainFrom, @TrainTo, @TestFrom, @TestTo,
            @WinnerTrialIndex, CAST(@Parameters AS jsonb), CAST(@TrainMetrics AS jsonb), CAST(@TestMetrics AS jsonb), @Status, @FailureCode,
            CAST(@Equity AS jsonb))
        ON CONFLICT (optimization_id, window_index) DO NOTHING;

        SELECT pg_notify('optimization_progress', @OptimizationId::text);
        """;

    public const string All =
        """
        SELECT window_index AS WindowIndex, train_start AS TrainStart, train_count AS TrainCount, test_start AS TestStart, test_count AS TestCount,
            train_from AS TrainFrom, train_to AS TrainTo, test_from AS TestFrom, test_to AS TestTo, winner_trial_index AS WinnerTrialIndex,
            parameters::text AS Parameters, train_metrics::text AS TrainMetrics, test_metrics::text AS TestMetrics, status AS Status,
            failure_code AS FailureCode, equity::text AS Equity
        FROM optimization_windows
        WHERE optimization_id = @OptimizationId
        ORDER BY window_index;
        """;

    public const string WriteSummary = "UPDATE optimizations SET walk_forward_summary = CAST(@Summary AS jsonb) WHERE id = @OptimizationId;";

    public const string Summary = "SELECT walk_forward_summary::text FROM optimizations WHERE id = @OptimizationId;";
}