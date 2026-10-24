namespace ProfitGraph.Infrastructure.Optimization.Repositories.Queries;

internal static class OptimizationReadQueries
{
    public const string Owner = "SELECT owner_id FROM optimizations WHERE id = @Id;";

    public const string Status = "SELECT status FROM background_jobs WHERE id = @Id;";

    public const string Cursor =
        """
        SELECT coalesce((SELECT max(t.sequence) FROM optimization_trials t WHERE t.optimization_id = j.id), 0) AS LastSequence, j.status AS Status
        FROM background_jobs j
        WHERE j.id = @Id;
        """;

    public const string Finalists = "SELECT id FROM backtest_runs WHERE optimization_id = @Id ORDER BY created_at, id;";

    public const string ForStrategy =
        """
        SELECT o.id AS Id, o.strategy_version AS StrategyVersion, o.created_at AS CreatedAt, o.configuration -> 'backtest' ->> 'venue' AS Venue,
            o.configuration -> 'backtest' ->> 'symbol' AS Symbol, o.configuration -> 'backtest' ->> 'timeframe' AS Timeframe,
            o.configuration ->> 'method' AS Method, j.status AS Status, o.completed + o.rejected_by_constraint + o.failed AS ExecutedTrials,
            o.best_objective AS BestObjective
        FROM optimizations o
        JOIN background_jobs j ON j.id = o.id
        WHERE o.strategy_id = @StrategyId
            AND (@After::uuid IS NULL OR (o.created_at, o.id) < (SELECT created_at, id FROM optimizations WHERE id = @After::uuid))
        ORDER BY o.created_at DESC, o.id DESC
        LIMIT @Take;
        """;
}