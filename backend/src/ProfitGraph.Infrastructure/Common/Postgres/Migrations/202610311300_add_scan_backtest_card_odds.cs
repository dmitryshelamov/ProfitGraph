using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     A scanner backtest keeps how its cards ended by kind and band apart from its result, so the pair's page reads the odds of the
///     latest backtest without its signals (ADR 0071, section 1).
/// </summary>
[Migration(202610311300)]
public sealed class AddScanBacktestCardOddsMigration : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE scan_backtests ADD COLUMN card_odds jsonb NULL;");
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE scan_backtests DROP COLUMN card_odds;");
    }
}
