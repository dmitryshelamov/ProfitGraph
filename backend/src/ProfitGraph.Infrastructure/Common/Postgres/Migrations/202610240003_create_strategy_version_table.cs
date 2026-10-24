using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610240003)]
public sealed class CreateStrategyVersionTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE strategy_versions (
                id uuid PRIMARY KEY,
                strategy_id uuid NOT NULL REFERENCES strategies (id),
                version_number integer NOT NULL CHECK (version_number > 0),
                snapshot_json jsonb NOT NULL,
                created_at timestamp with time zone NOT NULL,
                UNIQUE (strategy_id, version_number)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE strategy_versions;");
    }
}
