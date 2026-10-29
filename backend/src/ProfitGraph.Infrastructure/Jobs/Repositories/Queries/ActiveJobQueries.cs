namespace ProfitGraph.Infrastructure.Jobs.Repositories.Queries;

internal static class ActiveJobQueries
{
    /// <summary>
    ///     The owner's queued, running and paused jobs, lane by lane: the running job, then the queue in the Worker's order,
    ///     then paused jobs. <c>@Kinds</c> and <c>@Lanes</c> map each registered kind to its lane; a queued job's position
    ///     counts every user's queued jobs of the lane. Backtests and optimizations name their strategy and series from their
    ///     own rows, a historical download from its request.
    /// </summary>
    public const string ForOwner =
        """
        WITH lanes AS (
            SELECT * FROM unnest(@Kinds::text[], @Lanes::text[]) AS lane(kind, name)
        ),
        queue AS (
            SELECT j.id, row_number() OVER (PARTITION BY l.name ORDER BY j.created_at, j.id) AS position
            FROM background_jobs j
            JOIN lanes l ON l.kind = j.kind
            WHERE j.status = 'Queued'
        )
        SELECT j.id AS Id, j.kind AS Kind, l.name AS Lane, j.status AS Status, j.completed AS Completed, j.total AS Total,
            j.cancel_requested AS CancelRequested, j.pause_requested AS PauseRequested, j.created_at AS CreatedAt, j.started_at AS StartedAt,
            q.position AS QueuePosition, coalesce(r.strategy_id, o.strategy_id) AS StrategyId, s.name AS StrategyName,
            coalesce(r.strategy_version, o.strategy_version) AS StrategyVersion,
            coalesce(r.configuration ->> 'venue', o.configuration -> 'backtest' ->> 'venue', j.request ->> 'venue') AS Venue,
            coalesce(r.configuration ->> 'symbol', o.configuration -> 'backtest' ->> 'symbol', j.request ->> 'symbol') AS Symbol,
            coalesce(r.configuration ->> 'timeframe', o.configuration -> 'backtest' ->> 'timeframe', j.request ->> 'timeframe') AS Timeframe,
            o.configuration ->> 'method' AS Method, r.optimization_id AS OptimizationId
        FROM background_jobs j
        JOIN lanes l ON l.kind = j.kind
        LEFT JOIN queue q ON q.id = j.id
        LEFT JOIN backtest_runs r ON r.id = j.id
        LEFT JOIN optimizations o ON o.id = j.id
        LEFT JOIN strategies s ON s.id = coalesce(r.strategy_id, o.strategy_id)
        WHERE j.owner_id = @OwnerId AND j.status IN ('Queued', 'Running', 'Paused')
        ORDER BY l.name, CASE j.status WHEN 'Running' THEN 0 WHEN 'Queued' THEN 1 ELSE 2 END, j.created_at, j.id;
        """;

    public const string Heartbeat = "SELECT beat_at FROM worker_heartbeats WHERE name = @Name;";
}