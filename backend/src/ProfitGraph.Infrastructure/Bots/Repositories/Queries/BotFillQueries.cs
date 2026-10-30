namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotFillQueries
{
    internal const string Insert = """
                                   INSERT INTO bot_fills (id, bot_id, engine_order_id, side, tag, gross_base, gross_quote, fee, fee_asset, cumulative_gross_base, filled_at)
                                   VALUES (@Id, @BotId, @EngineOrderId, @Side, @Tag, @GrossBase, @GrossQuote, @Fee, @FeeAsset, @CumulativeGrossBase, @FilledAt)
                                   ON CONFLICT (bot_id, engine_order_id, cumulative_gross_base) DO NOTHING
                                   """;

    internal const string List = """
                                 SELECT id AS Id, sequence AS Sequence, bot_id AS BotId, engine_order_id AS EngineOrderId, side AS Side, tag AS Tag,
                                     gross_base AS GrossBase, gross_quote AS GrossQuote, fee AS Fee, fee_asset AS FeeAsset, cumulative_gross_base AS CumulativeGrossBase,
                                     filled_at AS FilledAt
                                 FROM bot_fills
                                 WHERE bot_id = @botId AND (@before::bigint IS NULL OR sequence < @before)
                                 ORDER BY sequence DESC
                                 LIMIT @limit
                                 """;

    internal const string ListFilled = """
                                       SELECT id AS Id, sequence AS Sequence, bot_id AS BotId, engine_order_id AS EngineOrderId, side AS Side, tag AS Tag,
                                           gross_base AS GrossBase, gross_quote AS GrossQuote, fee AS Fee, fee_asset AS FeeAsset, cumulative_gross_base AS CumulativeGrossBase,
                                           filled_at AS FilledAt
                                       FROM bot_fills
                                       WHERE bot_id = @botId AND filled_at >= @from AND filled_at < @to
                                       ORDER BY filled_at, sequence
                                       LIMIT @limit
                                       """;

    // A fill recorded before fee assets were stored paid a buy's fee in base and a sell's in quote, as the domain reads it.
    internal const string QuoteFees = """
                                      SELECT COALESCE(SUM(
                                          CASE WHEN COALESCE(f.fee_asset, CASE WHEN f.side = 'Buy' THEN 'Base' ELSE 'Quote' END) = 'Base'
                                              THEN f.fee * f.gross_quote / f.gross_base
                                              ELSE f.fee
                                          END), 0)
                                      FROM unnest(@froms::timestamptz[], @tos::timestamptz[]) WITH ORDINALITY AS r(from_time, to_time, position)
                                      LEFT JOIN bot_fills f ON f.bot_id = @botId AND f.filled_at >= r.from_time AND f.filled_at <= r.to_time
                                      GROUP BY r.position
                                      ORDER BY r.position
                                      """;
}