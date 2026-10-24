using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Each candle observation names the ledger and diagnostic ranges it owns, so its facts can be read without its
///     predecessor, and fact pages get their own indexes.
/// </summary>
[Migration(202610250001)]
public sealed class AddBacktestFactRangesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE backtest_candles
                ADD COLUMN ledger_from bigint NOT NULL DEFAULT 0 CHECK (ledger_from >= 0),
                ADD COLUMN diagnostic_from bigint NOT NULL DEFAULT 0 CHECK (diagnostic_from >= 0);

            UPDATE backtest_candles c SET ledger_from = previous.ledger_from, diagnostic_from = previous.diagnostic_from
            FROM (
                SELECT run_id, sequence,
                    coalesce(lag(ledger_through) OVER (PARTITION BY run_id ORDER BY sequence), 0) AS ledger_from,
                    coalesce(lag(diagnostic_through) OVER (PARTITION BY run_id ORDER BY sequence), 0) AS diagnostic_from
                FROM backtest_candles
            ) previous
            WHERE previous.run_id = c.run_id AND previous.sequence = c.sequence;

            ALTER TABLE backtest_candles
                ALTER COLUMN ledger_from DROP DEFAULT,
                ALTER COLUMN diagnostic_from DROP DEFAULT,
                ADD CHECK (ledger_from <= ledger_through AND diagnostic_from <= diagnostic_through);

            CREATE INDEX backtest_ledger_orders ON backtest_ledger (run_id, order_id, sequence) WHERE order_id IS NOT NULL;
            CREATE INDEX backtest_ledger_fills ON backtest_ledger (run_id, sequence) WHERE kind = 'Filled';
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP INDEX backtest_ledger_fills;
            DROP INDEX backtest_ledger_orders;
            ALTER TABLE backtest_candles DROP COLUMN diagnostic_from, DROP COLUMN ledger_from;
            """);
    }
}
