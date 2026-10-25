using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Stores a bot's seller fee schedule, read from its venue instead of the limit and market rates (ADR 0022, C3).
/// </summary>
[Migration(202610250019)]
public sealed class AddBotSellerFeesColumnMigration : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE bots ADD COLUMN seller_fees jsonb NULL;");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE bots DROP COLUMN seller_fees;");
    }
}
