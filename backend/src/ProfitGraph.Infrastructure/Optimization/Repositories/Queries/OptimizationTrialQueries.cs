namespace ProfitGraph.Infrastructure.Optimization.Repositories.Queries;

internal static class OptimizationTrialQueries
{
    private const string Columns =
        """
        optimization_id AS OptimizationId, sequence AS Sequence, trial_index AS TrialIndex, parameters::text AS Parameters, status AS Status,
        failure_code AS FailureCode, objective AS Objective, return_percent AS ReturnPercent, total_pnl AS TotalPnl,
        max_drawdown_percent AS MaxDrawdownPercent, executions AS Executions, elapsed_ms AS ElapsedMs, stage AS Stage,
        neighbors_evaluated AS NeighborsEvaluated, neighbors_eligible AS NeighborsEligible, neighborhood_mean AS NeighborhoodMean,
        window_index AS WindowIndex
        """;

    /// <summary>
    ///     A retried batch leaves trials already stored under their sequence unchanged.
    /// </summary>
    public const string Write =
        """
        INSERT INTO optimization_trials (optimization_id, sequence, window_index, trial_index, stage, parameters, status, failure_code, objective,
            return_percent, total_pnl, max_drawdown_percent, executions, elapsed_ms)
        SELECT @OptimizationId, t.sequence, t.window_index, t.trial_index, t.stage, t.parameters, t.status, t.failure_code, t.objective,
            t.return_percent, t.total_pnl, t.max_drawdown_percent, t.executions, t.elapsed_ms
        FROM jsonb_to_recordset(CAST(@Trials AS jsonb)) AS t (sequence bigint, window_index smallint, trial_index integer, stage smallint,
            parameters jsonb, status varchar,
            failure_code varchar, objective numeric, return_percent numeric, total_pnl numeric, max_drawdown_percent numeric, executions bigint,
            elapsed_ms bigint)
        ON CONFLICT (optimization_id, sequence) DO NOTHING;
        """;

    public const string WriteScores =
        """
        UPDATE optimization_trials o
        SET neighbors_evaluated = s.evaluated, neighbors_eligible = s.eligible, neighborhood_mean = s.mean
        FROM jsonb_to_recordset(CAST(@Scores AS jsonb)) AS s (trial_index integer, evaluated integer, eligible integer, mean numeric)
        WHERE o.optimization_id = @OptimizationId AND o.window_index = @WindowIndex AND o.trial_index = s.trial_index;
        """;

    public const string Page = $"SELECT {Columns} FROM optimization_trials WHERE optimization_id = @OptimizationId AND sequence > @After ORDER BY sequence LIMIT @Take;";

    public const string One =
        $"SELECT {Columns} FROM optimization_trials WHERE optimization_id = @OptimizationId AND window_index = @WindowIndex AND trial_index = @TrialIndex;";

    public const string All = $"SELECT {Columns} FROM optimization_trials WHERE optimization_id = @OptimizationId ORDER BY sequence;";
}