namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

internal static class SteamMarketReceiptQueries
{
    internal const string Next = "SELECT max(position) + 1 FROM steam_market_receipts WHERE credential_id = @credentialId";

    internal const string Lock = "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))";

    internal const string From = """
                                 SELECT position AS Position, receipt::text AS Receipt
                                 FROM steam_market_receipts
                                 WHERE credential_id = @credentialId AND position >= @position
                                 ORDER BY position
                                 """;

    internal const string Add = """
                                INSERT INTO steam_market_receipts (credential_id, position, receipt_id, receipt)
                                VALUES (@credentialId, @Position, @ReceiptId, CAST(@Receipt AS jsonb))
                                """;
}