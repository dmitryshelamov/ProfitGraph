using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610240002)]
public sealed class CreateStrategyTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE strategies (
                id uuid PRIMARY KEY,
                owner_id uuid NOT NULL REFERENCES users (id),
                name varchar(200) NOT NULL CHECK (length(trim(name)) > 0),
                current_version integer NOT NULL CHECK (current_version > 0),
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );
            CREATE INDEX strategies_owner_updated ON strategies (owner_id, updated_at DESC, id);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE strategies;");
    }
}
