using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Lets the owner name a bot; a bot without a name is shown by its symbol and timeframe.
/// </summary>
[Migration(202610260004)]
public sealed class AddBotNameMigration : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE bots ADD COLUMN name varchar(100) CHECK (name IS NULL OR length(trim(name)) > 0);");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE bots DROP COLUMN name;");
    }
}
