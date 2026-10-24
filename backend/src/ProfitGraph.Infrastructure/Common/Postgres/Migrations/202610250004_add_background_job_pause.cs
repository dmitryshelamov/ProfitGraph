using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250004)]
public sealed class AddBackgroundJobPauseMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE background_jobs DROP CONSTRAINT background_jobs_status_check;
            ALTER TABLE background_jobs ADD CONSTRAINT background_jobs_status_check
                CHECK (status IN ('Queued', 'Running', 'Paused', 'Completed', 'Failed', 'Cancelled'));
            ALTER TABLE background_jobs
                ADD COLUMN pause_requested boolean NOT NULL DEFAULT false,
                ADD COLUMN interruptions integer NOT NULL DEFAULT 0 CHECK (interruptions >= 0),
                ADD COLUMN advanced boolean NOT NULL DEFAULT false,
                ADD CONSTRAINT background_jobs_pause_running CHECK (NOT pause_requested OR status = 'Running');
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE background_jobs DROP CONSTRAINT background_jobs_pause_running;
            ALTER TABLE background_jobs DROP COLUMN advanced, DROP COLUMN interruptions, DROP COLUMN pause_requested;
            ALTER TABLE background_jobs DROP CONSTRAINT background_jobs_status_check;
            ALTER TABLE background_jobs ADD CONSTRAINT background_jobs_status_check
                CHECK (status IN ('Queued', 'Running', 'Completed', 'Failed', 'Cancelled'));
            """);
    }
}
