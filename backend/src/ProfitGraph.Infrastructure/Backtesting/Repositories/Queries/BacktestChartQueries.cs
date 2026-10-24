namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Queries;

/// <summary>
///     Chart buckets of consecutive candle observations (Profit Fox Designer ADR 0046). Fills count in the bucket of the
///     candle that reported them.
/// </summary>
internal static class BacktestChartQueries
{
    /// <summary>
    ///     Candles per bucket so that a run has at most <c>@MaxPoints</c> buckets.
    /// </summary>
    public const string BucketSize = "greatest(1, (candle_count + @MaxPoints - 1) / @MaxPoints)::integer";

    /// <summary>
    ///     The <c>candles</c>, <c>fills</c> and <c>buckets</c> expressions of a run, given a <c>size</c> expression.
    /// </summary>
    public const string Aggregate =
        """
        candles AS MATERIALIZED (
            SELECT c.*, ((c.sequence - 1) / size.value)::integer AS bucket
            FROM backtest_candles c CROSS JOIN size
            WHERE c.run_id = @RunId
        ), fills AS (
            SELECT c.bucket,
                count(*) FILTER (WHERE l.snapshot -> 'fill' ->> 'side' = 'Buy')::integer AS buy_fills,
                count(*) FILTER (WHERE l.snapshot -> 'fill' ->> 'side' = 'Sell')::integer AS sell_fills
            FROM backtest_ledger l
            JOIN candles c ON l.sequence > c.ledger_from AND l.sequence <= c.ledger_through
            WHERE l.run_id = @RunId AND l.kind = 'Filled'
            GROUP BY c.bucket
        ), buckets AS (
            SELECT b.*, coalesce(f.buy_fills, 0) AS buy_fills, coalesce(f.sell_fills, 0) AS sell_fills
            FROM (
                SELECT bucket AS bucket_index, min(sequence) AS first_sequence, max(sequence) AS last_sequence,
                    min(open_time) AS first_open_time, max(open_time) AS last_open_time, (array_agg(open ORDER BY sequence))[1] AS open,
                    max(high) AS high, min(low) AS low, (array_agg(close ORDER BY sequence DESC))[1] AS close, sum(volume) AS volume,
                    (array_agg(equity ORDER BY sequence DESC))[1] AS equity, min(equity) AS min_equity, max(equity) AS max_equity,
                    (array_agg(total_pnl ORDER BY sequence DESC))[1] AS total_pnl
                FROM candles
                GROUP BY bucket
            ) b
            LEFT JOIN fills f ON f.bucket = b.bucket_index
        )
        """;

    private const string Columns =
        """
        first_sequence AS FirstSequence, last_sequence AS LastSequence, first_open_time AS FirstOpenTime, last_open_time AS LastOpenTime,
        open AS Open, high AS High, low AS Low, close AS Close, volume AS Volume, equity AS Equity, min_equity AS MinEquity,
        max_equity AS MaxEquity, total_pnl AS TotalPnl, buy_fills AS BuyFills, sell_fills AS SellFills
        """;

    /// <summary>
    ///     The stored bucket size of a compacted run, or null, and the size its buckets would have now.
    /// </summary>
    public const string Sizes = $"SELECT chart_bucket_size AS Stored, {BucketSize} AS Live FROM backtest_runs WHERE id = @RunId;";

    public const string Stored =
        $"""
         SELECT {Columns}
         FROM backtest_chart_buckets
         WHERE run_id = @RunId
         ORDER BY bucket_index;
         """;

    /// <summary>
    ///     Buckets of a run that was never compacted, such as one interrupted by a Worker restart.
    /// </summary>
    public const string Live =
        $"""
         WITH size AS (
             SELECT {BucketSize} AS value FROM backtest_runs WHERE id = @RunId
         ), {Aggregate}
         SELECT {Columns}
         FROM buckets
         ORDER BY bucket_index;
         """;
}