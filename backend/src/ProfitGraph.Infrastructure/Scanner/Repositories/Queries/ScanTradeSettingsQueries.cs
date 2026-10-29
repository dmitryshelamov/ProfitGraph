namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScanTradeSettingsQueries
{
    internal const string Get = """
                                SELECT owner_id AS OwnerId, venue AS Venue, exchange_credential_id AS CredentialId, risk AS Risk, max_amount AS MaxAmount,
                                       strategy_id AS StrategyId, updated_at AS UpdatedAt
                                FROM scan_trade_settings
                                WHERE owner_id = @OwnerId AND venue = @Venue
                                """;

    internal const string Save = """
                                 INSERT INTO scan_trade_settings (owner_id, venue, exchange_credential_id, risk, max_amount, strategy_id, updated_at)
                                 VALUES (@OwnerId, @Venue, @CredentialId, @Risk, @MaxAmount, @StrategyId, @UpdatedAt)
                                 ON CONFLICT (owner_id, venue) DO UPDATE
                                 SET exchange_credential_id = EXCLUDED.exchange_credential_id, risk = EXCLUDED.risk, max_amount = EXCLUDED.max_amount,
                                     strategy_id = EXCLUDED.strategy_id, updated_at = EXCLUDED.updated_at
                                 """;
}