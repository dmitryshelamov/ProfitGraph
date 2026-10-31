using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     A backtest's closed card keeps the cautious ends of its chance and expected R, which prove auto trade rules, and a paper trade the
///     chance it was taken with, which the rule's page compares with how often paper trades reach their target (ADR 0073).
/// </summary>
[Migration(202611011200)]
public sealed class AddScanCardOddsCautiousColumnsMigration : Migration
{
    public override void Up()
    {
        Execute.Sql("""
                    ALTER TABLE scan_backtest_cards ADD COLUMN chance_low numeric NULL, ADD COLUMN expected_r_low numeric NULL;
                    ALTER TABLE scan_paper_trades ADD COLUMN chance numeric NULL;
                    """);
    }

    public override void Down()
    {
        Execute.Sql("""
                    ALTER TABLE scan_paper_trades DROP COLUMN chance;
                    ALTER TABLE scan_backtest_cards DROP COLUMN expected_r_low, DROP COLUMN chance_low;
                    """);
    }
}
