namespace ProfitGraph.Infrastructure.Jobs.Repositories.Queries;

internal static class BackgroundJobQueries
{
    /// <summary>
    ///     Notified with the job's identifier whenever a job finishes, so readers that follow a job learn its outcome.
    /// </summary>
    public const string FinishedChannel = "background_job_finished";

    private const string Columns =
        """
        id AS Id, kind AS Kind, owner_id AS OwnerId, status AS Status, request::text AS Request, result::text AS Result,
        failure::text AS Failure, completed AS Completed, total AS Total, cancel_requested AS CancelRequested,
        created_at AS CreatedAt, started_at AS StartedAt, finished_at AS FinishedAt, updated_at AS UpdatedAt
        """;

    public const string Add =
        """
        INSERT INTO background_jobs (id, kind, owner_id, status, request, result, failure, completed, total, cancel_requested,
                                     created_at, started_at, finished_at, updated_at)
        VALUES (@Id, @Kind, @OwnerId, @Status, CAST(@Request AS jsonb), CAST(@Result AS jsonb), CAST(@Failure AS jsonb), @Completed, @Total,
                @CancelRequested, @CreatedAt, @StartedAt, @FinishedAt, @UpdatedAt);
        """;

    public const string Get = $"SELECT {Columns} FROM background_jobs WHERE id = @Id;";

    public const string LockNextQueued =
        $"SELECT {Columns} FROM background_jobs WHERE status = 'Queued' AND kind = ANY(@Kinds) ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED;";

    /// <summary>
    ///     Applies a transition only from the expected status. Progress written by the runner is never lowered, a
    ///     cancellation request is never withdrawn, and only one request can set it.
    /// </summary>
    public const string Update =
        """
        UPDATE background_jobs
        SET status = @Status,
            result = CAST(@Result AS jsonb),
            failure = CAST(@Failure AS jsonb),
            completed = CASE WHEN @Completed >= completed THEN @Completed ELSE completed END,
            total = CASE WHEN @Completed >= completed THEN @Total ELSE total END,
            cancel_requested = cancel_requested OR @CancelRequested,
            started_at = @StartedAt,
            finished_at = @FinishedAt,
            updated_at = @UpdatedAt
        WHERE id = @Id AND status = @ExpectedStatus AND NOT (@SetsCancelRequest AND cancel_requested);

        SELECT pg_notify('background_job_finished', @Id::text) WHERE @FinishedAt::timestamp with time zone IS NOT NULL;
        """;

    public const string ReportProgress =
        """
        UPDATE background_jobs SET completed = @Completed, total = @Total, updated_at = @Now
        WHERE id = @Id AND status = 'Running' AND completed <= @Completed;
        """;

    public const string RestartProgress =
        """
        UPDATE background_jobs SET completed = @Completed, total = @Total, updated_at = @Now
        WHERE id = @Id AND status = 'Running';
        """;

    public const string IsCancelRequested = "SELECT cancel_requested FROM background_jobs WHERE id = @Id AND status = 'Running';";

    /// <summary>
    ///     Returns the identifiers of the interrupted jobs, each notified as finished.
    /// </summary>
    public const string InterruptRunning =
        """
        WITH interrupted AS (
            UPDATE background_jobs
            SET status = 'Failed', failure = CAST(@Failure AS jsonb), finished_at = @Now, updated_at = @Now
            WHERE status = 'Running' AND (@Id::uuid IS NULL OR id = @Id) AND (@Kinds::text[] IS NULL OR kind = ANY(@Kinds))
            RETURNING id
        )
        SELECT i.id FROM interrupted i CROSS JOIN LATERAL (SELECT pg_notify('background_job_finished', i.id::text)) n;
        """;

    public const string Beat =
        """
        INSERT INTO worker_heartbeats (name, beat_at) VALUES (@Name, @Now)
        ON CONFLICT (name) DO UPDATE SET beat_at = EXCLUDED.beat_at;
        """;
}