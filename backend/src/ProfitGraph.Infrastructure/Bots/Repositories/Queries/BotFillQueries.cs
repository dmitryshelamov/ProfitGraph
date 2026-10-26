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
}