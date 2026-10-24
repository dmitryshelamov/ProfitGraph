using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250007)]
public sealed class AddBacktestRunDrawdownColumnMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE backtest_runs ADD COLUMN max_drawdown_percent numeric NULL;

            UPDATE backtest_runs SET max_drawdown_percent = (metrics -> 'full' ->> 'maxDrawdownPercent')::numeric WHERE return_percent IS NOT NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE backtest_runs DROP COLUMN max_drawdown_percent;");
    }
}
