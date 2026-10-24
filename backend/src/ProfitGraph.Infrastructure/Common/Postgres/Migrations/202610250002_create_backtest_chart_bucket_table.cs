using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     The chart of a finished run: at most a fixed number of buckets of consecutive candles (Profit Fox Designer ADR 0046).
/// </summary>
[Migration(202610250002)]
public sealed class CreateBacktestChartBucketTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE backtest_chart_buckets (
                run_id uuid NOT NULL REFERENCES backtest_runs (id),
                bucket_index integer NOT NULL CHECK (bucket_index >= 0),
                first_sequence bigint NOT NULL,
                last_sequence bigint NOT NULL,
                first_open_time timestamp with time zone NOT NULL,
                last_open_time timestamp with time zone NOT NULL,
                open numeric NOT NULL,
                high numeric NOT NULL,
                low numeric NOT NULL,
                close numeric NOT NULL,
                volume numeric NOT NULL,
                equity numeric NOT NULL,
                min_equity numeric NOT NULL,
                max_equity numeric NOT NULL,
                total_pnl numeric NOT NULL,
                buy_fills integer NOT NULL,
                sell_fills integer NOT NULL,
                PRIMARY KEY (run_id, bucket_index)
            );

            ALTER TABLE backtest_runs
                ADD COLUMN chart_bucket_size integer NULL CHECK (chart_bucket_size > 0),
                ADD COLUMN return_percent numeric NULL;

            CREATE INDEX backtest_runs_ranking ON backtest_runs (owner_id, quote_asset, return_percent DESC, created_at DESC, id)
                WHERE return_percent IS NOT NULL;
            CREATE INDEX backtest_runs_uncompacted ON backtest_runs (id) WHERE chart_bucket_size IS NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP INDEX backtest_runs_uncompacted;
            DROP INDEX backtest_runs_ranking;
            ALTER TABLE backtest_runs DROP COLUMN return_percent, DROP COLUMN chart_bucket_size;
            DROP TABLE backtest_chart_buckets;
            """);
    }
}
