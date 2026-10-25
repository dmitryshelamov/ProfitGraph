using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     A bot's working orders that their venue holds locally, with why (ADR 0023, TC4): one row per waiting order.
/// </summary>
[Migration(202610250044)]
public sealed class CreateBotOrderWaitingTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bot_order_waiting (
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                engine_order_id bigint NOT NULL,
                code varchar(32) NOT NULL,
                since timestamp with time zone NOT NULL,
                until timestamp with time zone NULL,
                low numeric NULL CHECK (low > 0),
                high numeric NULL CHECK (high > 0),
                PRIMARY KEY (bot_id, engine_order_id),
                CHECK (low IS NULL OR high IS NULL OR low <= high)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE bot_order_waiting;");
    }
}
