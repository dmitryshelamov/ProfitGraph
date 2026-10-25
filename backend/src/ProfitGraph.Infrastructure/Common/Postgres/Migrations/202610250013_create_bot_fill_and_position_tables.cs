using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250013)]
public sealed class CreateBotFillAndPositionTablesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bot_fills (
                id uuid PRIMARY KEY,
                sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                engine_order_id bigint NOT NULL,
                side varchar(8) NOT NULL,
                tag varchar(128) NULL,
                gross_base numeric NOT NULL CHECK (gross_base > 0),
                gross_quote numeric NOT NULL CHECK (gross_quote > 0),
                fee numeric NOT NULL CHECK (fee >= 0),
                cumulative_gross_base numeric NOT NULL,
                filled_at timestamp with time zone NOT NULL,
                UNIQUE (bot_id, engine_order_id, cumulative_gross_base)
            );
            CREATE INDEX bot_fills_bot_sequence ON bot_fills (bot_id, sequence DESC);
            CREATE TABLE bot_position_cycles (
                id uuid PRIMARY KEY,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                opened_at timestamp with time zone NOT NULL,
                realized_pnl_at_open numeric NOT NULL,
                closed_at timestamp with time zone NULL,
                realized_pnl numeric NULL
            );
            CREATE UNIQUE INDEX bot_position_cycles_one_open ON bot_position_cycles (bot_id) WHERE closed_at IS NULL;
            CREATE INDEX bot_position_cycles_bot_opened ON bot_position_cycles (bot_id, opened_at DESC);
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE bot_position_cycles; DROP TABLE bot_fills;");
    }
}
