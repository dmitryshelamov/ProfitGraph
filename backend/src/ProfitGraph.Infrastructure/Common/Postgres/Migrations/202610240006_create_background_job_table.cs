using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610240006)]
public sealed class CreateBackgroundJobTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE background_jobs (
                id uuid PRIMARY KEY,
                kind varchar(64) NOT NULL,
                owner_id uuid NOT NULL REFERENCES users (id),
                status varchar(16) NOT NULL CHECK (status IN ('Queued', 'Running', 'Completed', 'Failed', 'Cancelled')),
                request jsonb NOT NULL,
                result jsonb NULL,
                failure jsonb NULL,
                completed bigint NOT NULL DEFAULT 0,
                total bigint NULL,
                cancel_requested boolean NOT NULL DEFAULT false,
                created_at timestamp with time zone NOT NULL,
                started_at timestamp with time zone NULL,
                finished_at timestamp with time zone NULL,
                updated_at timestamp with time zone NOT NULL,
                CHECK (completed >= 0 AND (total IS NULL OR completed <= total))
            );
            CREATE INDEX background_jobs_queue ON background_jobs (created_at, id) WHERE status = 'Queued';
            CREATE INDEX background_jobs_running ON background_jobs (id) WHERE status = 'Running';
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE background_jobs;");
    }
}
