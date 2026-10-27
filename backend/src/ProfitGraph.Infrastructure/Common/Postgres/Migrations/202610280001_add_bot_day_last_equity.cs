using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Stores the equity of a bot's last recorded live root, which the next UTC day takes as its daily loss baseline (ADR 0041). A stored
///     baseline gets its own equity as the last one, the best value known.
/// </summary>
[Migration(202610280001)]
public sealed class AddBotDayLastEquityMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE bots ADD COLUMN day_last_equity numeric(38, 18) NULL;
            UPDATE bots SET day_last_equity = day_start_equity WHERE day_start_utc IS NOT NULL;
            ALTER TABLE bots ADD CONSTRAINT bots_day_last_equity CHECK ((day_start_utc IS NULL) = (day_last_equity IS NULL));
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE bots
                DROP CONSTRAINT bots_day_last_equity,
                DROP COLUMN day_last_equity;
            """);
    }
}
