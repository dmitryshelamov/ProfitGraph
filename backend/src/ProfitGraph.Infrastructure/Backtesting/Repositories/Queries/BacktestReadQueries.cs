namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Queries;

internal static class BacktestReadQueries
{
    public const string Owner = "SELECT owner_id FROM backtest_runs WHERE id = @RunId;";

    public const string State =
        """
        SELECT j.status AS Status, r.outcome AS Outcome,
            coalesce((SELECT max(c.sequence) FROM backtest_candles c WHERE c.run_id = r.id), 0) AS LastSequence, r.ledger_count AS LedgerCount, r.candle_count AS CandleCount, r.diagnostic_count AS DiagnosticCount,
            r.chart_bucket_size AS ChartBucketSize
        FROM backtest_runs r
        JOIN background_jobs j ON j.id = r.id
        WHERE r.id = @RunId;
        """;

    public const string Observations =
        """
        SELECT sequence AS Sequence, open_time AS OpenTime, candle_index AS CandleIndex, phase AS Phase, market_price AS MarketPrice, open AS Open,
            high AS High, low AS Low, close AS Close, volume AS Volume, equity AS Equity, unrealized_pnl AS UnrealizedPnl, total_pnl AS TotalPnl,
            realized_pnl AS RealizedPnl, quote_total AS QuoteTotal, quote_reserved AS QuoteReserved, base_total AS BaseTotal,
            base_reserved AS BaseReserved, cost_basis AS CostBasis, fees_base AS FeesBase, fees_quote AS FeesQuote, last_buy_price AS LastBuyPrice,
            active_orders AS ActiveOrders, ledger_from AS LedgerFrom, ledger_through AS LedgerThrough, diagnostic_from AS DiagnosticFrom,
            diagnostic_through AS DiagnosticThrough
        FROM backtest_candles
        WHERE run_id = @RunId AND sequence > @After
        ORDER BY sequence
        LIMIT @Take;
        """;

    public const string LedgerRange =
        """
        SELECT sequence AS Sequence, order_id AS OrderId, snapshot::text AS Snapshot
        FROM backtest_ledger
        WHERE run_id = @RunId AND sequence > @From AND sequence <= @Through
        ORDER BY sequence;
        """;

    public const string DiagnosticRange =
        """
        SELECT sequence AS Sequence, snapshot::text AS Snapshot
        FROM backtest_diagnostics
        WHERE run_id = @RunId AND sequence > @From AND sequence <= @Through
        ORDER BY sequence;
        """;

    public const string Orders =
        """
        SELECT DISTINCT ON (order_id) sequence AS Sequence, order_id AS OrderId, snapshot::text AS Snapshot
        FROM backtest_ledger
        WHERE run_id = @RunId AND order_id > @After
        ORDER BY order_id, sequence DESC
        LIMIT @Take;
        """;

    public const string Fills =
        """
        SELECT sequence AS Sequence, order_id AS OrderId, snapshot::text AS Snapshot
        FROM backtest_ledger
        WHERE run_id = @RunId AND kind = 'Filled' AND sequence > @After
        ORDER BY sequence
        LIMIT @Take;
        """;

    public const string Diagnostics =
        """
        SELECT sequence AS Sequence, snapshot::text AS Snapshot
        FROM backtest_diagnostics
        WHERE run_id = @RunId AND sequence > @After
        ORDER BY sequence
        LIMIT @Take;
        """;

    private const string Summary =
        """
        SELECT r.id AS Id, r.strategy_id AS StrategyId, s.name AS StrategyName, r.strategy_version AS StrategyVersion,
            r.configuration ->> 'venue' AS Venue, r.configuration ->> 'symbol' AS Symbol, r.configuration ->> 'timeframe' AS Timeframe,
            r.created_at AS CreatedAt, j.status AS Status, r.outcome AS Outcome, r.quote_asset AS QuoteAsset, r.total_pnl AS TotalPnl,
            r.return_percent AS ReturnPercent
        FROM backtest_runs r
        JOIN background_jobs j ON j.id = r.id
        JOIN strategies s ON s.id = r.strategy_id
        """;

    public const string ForStrategy =
        $"""
         {Summary}
         WHERE r.strategy_id = @StrategyId
             AND (@After::uuid IS NULL OR (r.created_at, r.id) < (SELECT created_at, id FROM backtest_runs WHERE id = @After::uuid))
         ORDER BY r.created_at DESC, r.id DESC
         LIMIT @Take;
         """;

    public const string Ranking =
        $"""
         {Summary}
         WHERE r.owner_id = @OwnerId AND r.quote_asset = @Quote AND r.return_percent IS NOT NULL
             AND (@Symbol::text IS NULL OR r.configuration ->> 'symbol' = @Symbol::text)
             AND (@Timeframe::text IS NULL OR r.configuration ->> 'timeframe' = @Timeframe::text)
             AND (@After::uuid IS NULL OR (r.return_percent, r.created_at, r.id) <
                 (SELECT return_percent, created_at, id FROM backtest_runs WHERE id = @After::uuid))
         ORDER BY r.return_percent DESC, r.created_at DESC, r.id DESC
         LIMIT @Take;
         """;

    public const string Latest =
        $"""
         SELECT DISTINCT ON (StrategyId) * FROM (
             {Summary}
             WHERE r.strategy_id = ANY(@StrategyIds)
         ) runs
         ORDER BY StrategyId, CreatedAt DESC, Id DESC;
         """;
}