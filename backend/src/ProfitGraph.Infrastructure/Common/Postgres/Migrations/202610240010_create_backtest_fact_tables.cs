using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

/// <summary>
///     Immutable facts of a run (ADR 0012): ledger entries, one numeric observation per replayed candle with how far the
///     ledger and diagnostics had got, and diagnostics.
/// </summary>
[Migration(202610240010)]
public sealed class CreateBacktestFactTablesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE backtest_ledger (
                run_id uuid NOT NULL REFERENCES backtest_runs (id),
                sequence bigint NOT NULL CHECK (sequence > 0),
                candle_index integer NOT NULL,
                kind varchar(16) NOT NULL,
                order_id bigint NULL,
                snapshot jsonb NOT NULL,
                PRIMARY KEY (run_id, sequence)
            );

            CREATE TABLE backtest_candles (
                run_id uuid NOT NULL REFERENCES backtest_runs (id),
                sequence bigint NOT NULL CHECK (sequence > 0),
                open_time timestamp with time zone NOT NULL,
                candle_index integer NOT NULL,
                phase varchar(16) NOT NULL,
                market_price numeric NOT NULL,
                open numeric NOT NULL,
                high numeric NOT NULL,
                low numeric NOT NULL,
                close numeric NOT NULL,
                volume numeric NOT NULL,
                equity numeric NOT NULL,
                unrealized_pnl numeric NOT NULL,
                total_pnl numeric NOT NULL,
                realized_pnl numeric NOT NULL,
                quote_total numeric NOT NULL,
                quote_reserved numeric NOT NULL,
                base_total numeric NOT NULL,
                base_reserved numeric NOT NULL,
                cost_basis numeric NOT NULL,
                fees_base numeric NOT NULL,
                fees_quote numeric NOT NULL,
                last_buy_price numeric NULL,
                active_orders integer NOT NULL CHECK (active_orders >= 0),
                ledger_through bigint NOT NULL CHECK (ledger_through >= 0),
                diagnostic_through bigint NOT NULL CHECK (diagnostic_through >= 0),
                PRIMARY KEY (run_id, sequence)
            );

            CREATE TABLE backtest_diagnostics (
                run_id uuid NOT NULL REFERENCES backtest_runs (id),
                sequence bigint NOT NULL CHECK (sequence > 0),
                code varchar(64) NOT NULL,
                candle_index integer NULL,
                snapshot jsonb NOT NULL,
                PRIMARY KEY (run_id, sequence)
            );
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE backtest_diagnostics; DROP TABLE backtest_candles; DROP TABLE backtest_ledger;");
    }
}
