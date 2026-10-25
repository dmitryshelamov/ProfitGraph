using FluentMigrator;

namespace ProfitGraph.Venues.TInvest.Persistence.Migrations;

/// <summary>
///     DAY-order carry-over (ADR 0023, T12b): who asked to cancel an attempt, the trading date a carried remainder waits to
///     leave, and each attempt's own executed lots, quote and fee once final.
/// </summary>
[Migration(202610250047)]
public sealed class AddTInvestOrderAttemptCarryOverMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE tinvest_order_attempts DROP CONSTRAINT tinvest_order_attempts_waiting_code_check;
            ALTER TABLE tinvest_order_attempts ADD CONSTRAINT tinvest_order_attempts_waiting_code_check
                CHECK (waiting_code IN ('PriceBand', 'SessionClosed', 'CarryOver'));
            ALTER TABLE tinvest_order_attempts
                ADD COLUMN cancelled_by varchar(16) NULL CHECK (cancelled_by IN ('Owner', 'CarryOver')),
                ADD COLUMN carried_from date NULL,
                ADD COLUMN executed_lots bigint NOT NULL DEFAULT 0 CHECK (executed_lots >= 0),
                ADD COLUMN executed_quote numeric NOT NULL DEFAULT 0 CHECK (executed_quote >= 0),
                ADD COLUMN executed_fee numeric NOT NULL DEFAULT 0 CHECK (executed_fee >= 0);
            CREATE INDEX ix_tinvest_order_attempts_working ON tinvest_order_attempts (bot_id) WHERE state = 'Active';
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP INDEX ix_tinvest_order_attempts_working;
            ALTER TABLE tinvest_order_attempts DROP COLUMN executed_fee, DROP COLUMN executed_quote, DROP COLUMN executed_lots, DROP COLUMN carried_from,
                DROP COLUMN cancelled_by;
            ALTER TABLE tinvest_order_attempts DROP CONSTRAINT tinvest_order_attempts_waiting_code_check;
            ALTER TABLE tinvest_order_attempts ADD CONSTRAINT tinvest_order_attempts_waiting_code_check CHECK (waiting_code IN ('PriceBand', 'SessionClosed'));
            """);
    }
}
