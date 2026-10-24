using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610240007)]
public sealed class CreateWorkerHeartbeatTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE worker_heartbeats (
                name varchar(64) PRIMARY KEY,
                beat_at timestamp with time zone NOT NULL
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE worker_heartbeats;");
    }
}
