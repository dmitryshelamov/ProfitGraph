namespace ProfitGraph.Infrastructure.MarketData.Repositories.Queries;

internal static class CandleQueries
{
    /// <summary>
    ///     Upserts a batch, touching only rows whose values differ. The advisory lock orders concurrent imports of one
    ///     series, so batches arriving in different orders cannot deadlock on the same rows.
    /// </summary>
    public const string Import =
        """
        SELECT pg_advisory_xact_lock(hashtextextended(@Venue || ':' || @Symbol || ':' || @Timeframe, 0));
        INSERT INTO candles AS stored (venue, symbol, timeframe, open_time, open, high, low, close, volume, source, imported_at)
        SELECT @Venue, @Symbol, @Timeframe, open_time, open, high, low, close, volume, 'csv', @ImportedAt
        FROM unnest(@OpenTimes::timestamptz[], @Opens::numeric[], @Highs::numeric[], @Lows::numeric[], @Closes::numeric[], @Volumes::numeric[])
            AS incoming(open_time, open, high, low, close, volume)
        ORDER BY open_time
        ON CONFLICT (venue, symbol, timeframe, open_time) DO UPDATE SET
            open = EXCLUDED.open,
            high = EXCLUDED.high,
            low = EXCLUDED.low,
            close = EXCLUDED.close,
            volume = EXCLUDED.volume,
            source = EXCLUDED.source,
            imported_at = EXCLUDED.imported_at
        WHERE (stored.open, stored.high, stored.low, stored.close, stored.volume) IS DISTINCT FROM
              (EXCLUDED.open, EXCLUDED.high, EXCLUDED.low, EXCLUDED.close, EXCLUDED.volume);
        """;

    /// <summary>
    ///     Adds only missing candles; stored values and their source stay as they are.
    /// </summary>
    public const string Append =
        """
        INSERT INTO candles (venue, symbol, timeframe, open_time, open, high, low, close, volume, source, imported_at)
        SELECT @Venue, @Symbol, @Timeframe, open_time, open, high, low, close, volume, @Source, @ImportedAt
        FROM unnest(@OpenTimes::timestamptz[], @Opens::numeric[], @Highs::numeric[], @Lows::numeric[], @Closes::numeric[], @Volumes::numeric[])
            AS incoming(open_time, open, high, low, close, volume)
        ORDER BY open_time
        ON CONFLICT (venue, symbol, timeframe, open_time) DO NOTHING;
        """;

    public const string Get =
        """
        SELECT open_time AS OpenTime, open AS Open, high AS High, low AS Low, close AS Close, volume AS Volume
        FROM candles
        WHERE venue = @Venue AND symbol = @Symbol AND timeframe = @Timeframe AND open_time >= @From AND open_time < @To
        ORDER BY open_time
        LIMIT @Limit;
        """;
}