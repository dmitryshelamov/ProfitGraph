namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

internal static class SteamMarketAttributionQueries
{
    internal const string Of = """
                               SELECT receipt_id AS ReceiptId, client_order_id AS ClientOrderId, bot_id AS BotId
                               FROM steam_market_attributions
                               WHERE credential_id = @credentialId
                               ORDER BY attributed_at, receipt_id
                               """;

    internal const string Add = """
                                INSERT INTO steam_market_attributions (credential_id, receipt_id, client_order_id, bot_id, attributed_at)
                                VALUES (@credentialId, @ReceiptId, @ClientOrderId, @BotId, @at)
                                """;
}