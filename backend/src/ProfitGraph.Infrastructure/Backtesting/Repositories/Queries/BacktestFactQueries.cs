namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Queries;

/// <summary>
///     Inserts a batch of facts from JSON arrays, advances the run's fact counters and signals progress, which listeners
///     receive only after the commit. A fact already stored is a primary key violation, never an update.
/// </summary>
internal static class BacktestFactQueries
{
    public const string Append =
        """
        INSERT INTO backtest_ledger (run_id, sequence, candle_index, kind, order_id, snapshot)
        SELECT @RunId, row.sequence, row."candleIndex", row.kind, row."orderId", row.snapshot
        FROM jsonb_to_recordset(CAST(@Ledger AS jsonb)) AS row(sequence bigint, "candleIndex" integer, kind varchar(16), "orderId" bigint, snapshot jsonb);

        INSERT INTO backtest_candles (
            run_id, sequence, open_time, candle_index, phase, market_price, open, high, low, close, volume, equity, unrealized_pnl, total_pnl,
            realized_pnl, quote_total, quote_reserved, base_total, base_reserved, cost_basis, fees_base, fees_quote, last_buy_price, active_orders,
            ledger_from, ledger_through, diagnostic_from, diagnostic_through)
        SELECT @RunId, row.sequence, row."openTime", row."candleIndex", row.phase, row."marketPrice", row.open, row.high, row.low, row.close,
            row.volume, row.equity, row."unrealizedPnl", row."totalPnl", row."realizedPnl", row."quoteTotal", row."quoteReserved", row."baseTotal",
            row."baseReserved", row."costBasis", row."feesBase", row."feesQuote", row."lastBuyPrice", row."activeOrders", row."ledgerFrom",
            row."ledgerThrough", row."diagnosticFrom", row."diagnosticThrough"
        FROM jsonb_to_recordset(CAST(@Candles AS jsonb)) AS row(
            sequence bigint, "openTime" timestamptz, "candleIndex" integer, phase varchar(16), "marketPrice" numeric, open numeric, high numeric,
            low numeric, close numeric, volume numeric, equity numeric, "unrealizedPnl" numeric, "totalPnl" numeric, "realizedPnl" numeric,
            "quoteTotal" numeric, "quoteReserved" numeric, "baseTotal" numeric, "baseReserved" numeric, "costBasis" numeric, "feesBase" numeric,
            "feesQuote" numeric, "lastBuyPrice" numeric, "activeOrders" integer, "ledgerFrom" bigint, "ledgerThrough" bigint,
            "diagnosticFrom" bigint, "diagnosticThrough" bigint);

        INSERT INTO backtest_diagnostics (run_id, sequence, code, candle_index, snapshot)
        SELECT @RunId, row.sequence, row.code, row."candleIndex", row.snapshot
        FROM jsonb_to_recordset(CAST(@Diagnostics AS jsonb)) AS row(sequence bigint, code varchar(64), "candleIndex" integer, snapshot jsonb);

        UPDATE backtest_runs SET
            ledger_count = ledger_count + @LedgerCount,
            candle_count = candle_count + @CandleCount,
            diagnostic_count = diagnostic_count + @DiagnosticCount
        WHERE id = @RunId;

        SELECT pg_notify('backtest_progress', @RunId::text);
        """;
}