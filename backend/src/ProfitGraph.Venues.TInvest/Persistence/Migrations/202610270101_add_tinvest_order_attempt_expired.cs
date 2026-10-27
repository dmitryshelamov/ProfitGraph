using FluentMigrator;

namespace ProfitGraph.Venues.TInvest.Persistence.Migrations;

/// <summary>
///     A DAY order the exchange ended with its trading day (ADR 0031): an attempt may be recorded as cancelled by
///     <c>Expired</c>.
/// </summary>
[Migration(202610270101)]
public sealed class AddTInvestOrderAttemptExpiredMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE tinvest_order_attempts DROP CONSTRAINT tinvest_order_attempts_cancelled_by_check;
            ALTER TABLE tinvest_order_attempts ADD CONSTRAINT tinvest_order_attempts_cancelled_by_check
                CHECK (cancelled_by IN ('Owner', 'CarryOver', 'Expired'));
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE tinvest_order_attempts DROP CONSTRAINT tinvest_order_attempts_cancelled_by_check;
            ALTER TABLE tinvest_order_attempts ADD CONSTRAINT tinvest_order_attempts_cancelled_by_check CHECK (cancelled_by IN ('Owner', 'CarryOver'));
            """);
    }
}
