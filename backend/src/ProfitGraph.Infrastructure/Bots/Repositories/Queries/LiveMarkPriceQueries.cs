namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class LiveMarkPriceQueries
{
    private const string Columns = "venue AS Venue, symbol AS Symbol, price AS Price, observed_at AS ObservedAt";

    /// <summary>
    ///     An older observation never replaces a newer one; a stored one signals <c>live_mark_price</c> with the row.
    /// </summary>
    internal const string Store = """
                                  WITH stored AS (
                                      INSERT INTO live_mark_prices (id, venue, symbol, price, observed_at)
                                      VALUES (@id, @Venue, @Symbol, @Price, @ObservedAt)
                                      ON CONFLICT (venue, symbol) DO UPDATE SET price = EXCLUDED.price, observed_at = EXCLUDED.observed_at
                                      WHERE live_mark_prices.observed_at < EXCLUDED.observed_at
                                      RETURNING id)
                                  SELECT count(pg_notify('live_mark_price', id::text)) FROM stored
                                  """;

    internal const string Get = $"SELECT {Columns} FROM live_mark_prices WHERE venue = @venue AND symbol = @symbol";

    internal const string GetBySignal = $"SELECT {Columns} FROM live_mark_prices WHERE id = @id";
}