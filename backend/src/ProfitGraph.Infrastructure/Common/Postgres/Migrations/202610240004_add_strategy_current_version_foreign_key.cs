using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     The current version must exist; the check is deferred to commit because a strategy and its first version are inserted together.
/// </summary>
[Migration(202610240004)]
public sealed class AddStrategyCurrentVersionForeignKeyMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE strategies ADD CONSTRAINT strategies_current_version_fk
                FOREIGN KEY (id, current_version) REFERENCES strategy_versions (strategy_id, version_number)
                DEFERRABLE INITIALLY DEFERRED;
            """);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE strategies DROP CONSTRAINT strategies_current_version_fk;");
    }
}
