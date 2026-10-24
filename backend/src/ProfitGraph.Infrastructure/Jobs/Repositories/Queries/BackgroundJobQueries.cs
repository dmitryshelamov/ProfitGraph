namespace ProfitGraph.Infrastructure.Jobs.Repositories.Queries;

internal static class BackgroundJobQueries
{
    /// <summary>
    ///     Notified with the job's identifier whenever a job's status changes, so readers that follow a job learn when it
    ///     pauses, resumes or ends.
    /// </summary>
    public const string StatusChannel = "background_job_status";

    private const string Columns =
        """
        id AS Id, kind AS Kind, owner_id AS OwnerId, status AS Status, request::text AS Request, result::text AS Result,
        failure::text AS Failure, completed AS Completed, total AS Total, cancel_requested AS CancelRequested,
        pause_requested AS PauseRequested, interruptions AS Interruptions, advanced AS Advanced,
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

    public const string ListRunning = $"SELECT {Columns} FROM background_jobs WHERE status = 'Running' AND kind = ANY(@Kinds) ORDER BY created_at, id;";

    /// <summary>
    ///     Applies a transition only from the expected status. Progress written by the runner is never lowered except by a
    ///     claim, which starts an attempt with its own progress and no stored work yet; a cancellation or pause request is
    ///     never withdrawn while the job runs, and only one request can set each.
    /// </summary>
    public const string Update =
        """
        UPDATE background_jobs
        SET status = @Status,
            result = CAST(@Result AS jsonb),
            failure = CAST(@Failure AS jsonb),
            completed = CASE WHEN @ExpectedStatus = 'Queued' OR @Completed >= completed THEN @Completed ELSE completed END,
            total = CASE WHEN @ExpectedStatus = 'Queued' OR @Completed >= completed THEN @Total ELSE total END,
            cancel_requested = cancel_requested OR @CancelRequested,
            pause_requested = CASE WHEN @Status = 'Running' THEN pause_requested OR @PauseRequested ELSE false END,
            interruptions = @Interruptions,
            advanced = CASE WHEN @ExpectedStatus = 'Queued' THEN false ELSE advanced END,
            started_at = @StartedAt,
            finished_at = @FinishedAt,
            updated_at = @UpdatedAt
        WHERE id = @Id AND status = @ExpectedStatus AND NOT (@SetsCancelRequest AND cancel_requested) AND NOT (@SetsPauseRequest AND pause_requested);

        SELECT pg_notify('background_job_status', @Id::text) WHERE @Status <> @ExpectedStatus;
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

    public const string RecordStoredWork = "UPDATE background_jobs SET advanced = true WHERE id = @Id AND status = 'Running';";

    public const string StopRequest =
        "SELECT cancel_requested AS CancelRequested, pause_requested AS PauseRequested FROM background_jobs WHERE id = @Id AND status = 'Running';";

    public const string Beat =
        """
        INSERT INTO worker_heartbeats (name, beat_at) VALUES (@Name, @Now)
        ON CONFLICT (name) DO UPDATE SET beat_at = EXCLUDED.beat_at;
        """;
}