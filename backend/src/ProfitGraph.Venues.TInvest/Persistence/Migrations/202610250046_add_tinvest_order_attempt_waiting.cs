using FluentMigrator;

namespace ProfitGraph.Venues.TInvest.Persistence.Migrations;

/// <summary>
///     Why an attempt not yet sent waits locally (ADR 0023, T12): a closed trading status or the price band, with the band
///     bounds last seen. Only a planned attempt waits.
/// </summary>
[Migration(202610250046)]
public sealed class AddTInvestOrderAttemptWaitingMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE tinvest_order_attempts
                ADD COLUMN waiting_code varchar(32) NULL CHECK (waiting_code IN ('PriceBand', 'SessionClosed')),
                ADD COLUMN band_low numeric NULL CHECK (band_low > 0),
                ADD COLUMN band_high numeric NULL CHECK (band_high > 0),
                ADD CONSTRAINT ck_tinvest_order_attempts_waiting CHECK (waiting_code IS NULL OR state = 'Planned');
            CREATE INDEX ix_tinvest_order_attempts_waiting ON tinvest_order_attempts (bot_id) WHERE waiting_code IS NOT NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP INDEX ix_tinvest_order_attempts_waiting;
            ALTER TABLE tinvest_order_attempts DROP CONSTRAINT ck_tinvest_order_attempts_waiting, DROP COLUMN band_high, DROP COLUMN band_low,
                DROP COLUMN waiting_code;
            """);
    }
}
