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
            r.return_percent AS ReturnPercent, r.max_drawdown_percent AS MaxDrawdownPercent, r.optimization_id AS OptimizationId
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

    public const string Latest =
        $"""
         SELECT DISTINCT ON (StrategyId) * FROM (
             {Summary}
             WHERE r.strategy_id = ANY(@StrategyIds)
         ) runs
         ORDER BY StrategyId, CreatedAt DESC, Id DESC;
         """;

    /// <summary>
    ///     The filter values of the owner's finished runs with their counts, most runs first in each dimension. Quotes count
    ///     the completed runs with a result only, the population the ranking compares.
    /// </summary>
    public const string Facets =
        $"""
         WITH finished AS (
             SELECT r.strategy_id, s.name, r.quote_asset, r.optimization_id, r.configuration ->> 'venue' AS venue,
                 r.configuration ->> 'timeframe' AS timeframe, {StatusCategory} AS category
             FROM backtest_runs r
             JOIN background_jobs j ON j.id = r.id
             JOIN strategies s ON s.id = r.strategy_id
             WHERE r.owner_id = @OwnerId
         )
         SELECT 'quote' AS Dimension, quote_asset AS Value, NULL AS Name, count(*)::int AS Count FROM finished WHERE category = 'completed' GROUP BY quote_asset
         UNION ALL
         SELECT 'status', category, NULL, count(*)::int FROM finished WHERE category IS NOT NULL GROUP BY category
         UNION ALL
         SELECT 'origin', CASE WHEN optimization_id IS NULL THEN 'manual' ELSE 'optimization' END, NULL, count(*)::int
         FROM finished WHERE category IS NOT NULL GROUP BY 2
         UNION ALL
         SELECT 'venue', venue, NULL, count(*)::int FROM finished WHERE category IS NOT NULL GROUP BY venue
         UNION ALL
         SELECT 'timeframe', timeframe, NULL, count(*)::int FROM finished WHERE category IS NOT NULL GROUP BY timeframe
         UNION ALL
         SELECT 'strategy', strategy_id::text, name, count(*)::int FROM finished WHERE category IS NOT NULL GROUP BY strategy_id, name
         ORDER BY Dimension, Count DESC, Value;
         """;

    /// <summary>
    ///     How a run ended for the lists: completed with a result, failed or cancelled; null while it is queued or running.
    ///     A job that completed without a result ended with the engine's failed or cancelled outcome.
    /// </summary>
    private const string StatusCategory =
        """
        CASE WHEN r.return_percent IS NOT NULL THEN 'completed'
            WHEN j.status = 'Failed' OR r.outcome = 'failed' THEN 'failed'
            WHEN j.status = 'Cancelled' OR r.outcome = 'cancelled' THEN 'cancelled'
        END
        """;

    /// <summary>
    ///     The ranking ordered by one whitelisted column, with start time and identifier breaking ties in the same direction
    ///     so that the cursor row compares as a tuple. A null filter parameter does not filter; <c>@Search</c> is an
    ///     escaped <c>ILIKE</c> pattern.
    /// </summary>
    public static string Ranking(string column, bool ascending)
    {
        string direction = ascending ? "ASC" : "DESC";
        string compare = ascending ? ">" : "<";

        return $"""
                {Summary}
                WHERE r.owner_id = @OwnerId AND {StatusCategory} = @Status AND {column} IS NOT NULL
                    AND (@Quote::text IS NULL OR r.quote_asset = @Quote::text)
                    AND (@StrategyId::uuid IS NULL OR r.strategy_id = @StrategyId::uuid)
                    AND (@Venue::text IS NULL OR r.configuration ->> 'venue' = @Venue::text)
                    AND (@Symbol::text IS NULL OR r.configuration ->> 'symbol' = @Symbol::text)
                    AND (@Search::text IS NULL OR r.configuration ->> 'symbol' ILIKE @Search::text ESCAPE '\' OR s.name ILIKE @Search::text ESCAPE '\')
                    AND (@Timeframe::text IS NULL OR r.configuration ->> 'timeframe' = @Timeframe::text)
                    AND (@Origin::text IS NULL OR (r.optimization_id IS NOT NULL) = (@Origin::text = 'optimization'))
                    AND (@From::timestamptz IS NULL OR r.created_at >= @From::timestamptz)
                    AND (@To::timestamptz IS NULL OR r.created_at < @To::timestamptz)
                    AND (@MinReturn::numeric IS NULL OR r.return_percent >= @MinReturn::numeric)
                    AND (@MaxDrawdown::numeric IS NULL OR r.max_drawdown_percent <= @MaxDrawdown::numeric)
                    AND (@After::uuid IS NULL OR ({column}, r.created_at, r.id) {compare}
                        (SELECT {column.Replace("r.", "c.")}, c.created_at, c.id FROM backtest_runs c WHERE c.id = @After::uuid))
                ORDER BY {column} {direction}, r.created_at {direction}, r.id {direction}
                LIMIT @Take;
                """;
    }
}