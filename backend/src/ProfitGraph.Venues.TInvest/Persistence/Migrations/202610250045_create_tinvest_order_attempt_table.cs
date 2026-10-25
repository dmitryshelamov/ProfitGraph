using FluentMigrator;

namespace ProfitGraph.Venues.TInvest.Persistence.Migrations;

/// <summary>
///     The T-Invest order attempts (ADR 0023): each attempt of a bot's engine order under its deterministic request ID,
///     recorded before the place request may leave the process, with the exchange order ID it maps to.
/// </summary>
[Migration(202610250045)]
public sealed class CreateTInvestOrderAttemptTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE tinvest_order_attempts (
                request_id uuid PRIMARY KEY,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                engine_order_id bigint NOT NULL CHECK (engine_order_id > 0),
                attempt integer NOT NULL CHECK (attempt > 0),
                instrument_uid varchar(64) NOT NULL,
                side varchar(8) NOT NULL CHECK (side IN ('Buy', 'Sell')),
                order_type varchar(8) NOT NULL CHECK (order_type IN ('Limit', 'Market')),
                lots bigint NOT NULL CHECK (lots > 0),
                price numeric NULL CHECK (price > 0),
                state varchar(16) NOT NULL CHECK (state IN ('Planned', 'Sent', 'Active', 'Final', 'Unknown')),
                exchange_order_id varchar(64) NULL,
                broker_code integer NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                UNIQUE (bot_id, engine_order_id, attempt),
                CHECK ((order_type = 'Limit') = (price IS NOT NULL))
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE tinvest_order_attempts;");
    }
}
