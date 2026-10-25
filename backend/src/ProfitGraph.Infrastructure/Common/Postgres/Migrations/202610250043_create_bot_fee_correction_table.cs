using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Late commissions a venue reported for already accounted fills, recorded beside the unchanged fills, and the asset
///     a fill's fee was paid in, which no longer follows only the side (ADR 0023, TC4). Existing fills keep null.
/// </summary>
[Migration(202610250043)]
public sealed class CreateBotFeeCorrectionTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE bot_fills ADD COLUMN fee_asset varchar(8) NULL;
            CREATE TABLE bot_fee_corrections (
                id uuid PRIMARY KEY,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                engine_order_id bigint NOT NULL,
                previous_fee numeric NOT NULL CHECK (previous_fee >= 0),
                corrected_fee numeric NOT NULL,
                corrected_at timestamp with time zone NOT NULL,
                UNIQUE (bot_id, engine_order_id, corrected_fee),
                CHECK (corrected_fee > previous_fee)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE bot_fee_corrections; ALTER TABLE bot_fills DROP COLUMN fee_asset;");
    }
}
