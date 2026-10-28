namespace ProfitGraph.Infrastructure.Accounts.Repositories.Queries;

internal static class AccountSnapshotQueries
{
    internal const string Insert = """
                                   INSERT INTO account_value_snapshots (id, credential_id, captured_at, bucket_start, total_value, value_currency, available_value, coins)
                                   VALUES (@Id, @CredentialId, @CapturedAt, @BucketStart, @TotalValue, @ValueCurrency, @AvailableValue, CAST(@Coins AS jsonb))
                                   ON CONFLICT (credential_id, bucket_start) DO NOTHING
                                   """;

    internal const string List = """
                                 SELECT id AS Id, credential_id AS CredentialId, captured_at AS CapturedAt, bucket_start AS BucketStart,
                                     total_value AS TotalValue, value_currency AS ValueCurrency, available_value AS AvailableValue, coins::text AS Coins
                                 FROM account_value_snapshots
                                 WHERE credential_id = @credentialId AND captured_at >= @from AND captured_at < @to
                                 ORDER BY captured_at
                                 LIMIT @limit
                                 """;

    internal const string Latest = """
                                   SELECT id AS Id, credential_id AS CredentialId, captured_at AS CapturedAt, bucket_start AS BucketStart,
                                       total_value AS TotalValue, value_currency AS ValueCurrency, available_value AS AvailableValue, coins::text AS Coins
                                   FROM account_value_snapshots
                                   WHERE credential_id = @credentialId
                                   ORDER BY captured_at DESC
                                   LIMIT 1
                                   """;

    internal const string ThinToHourly = """
                                         DELETE FROM account_value_snapshots AS snapshot
                                         WHERE snapshot.captured_at < @before
                                             AND EXISTS (
                                                 SELECT 1 FROM account_value_snapshots AS later
                                                 WHERE later.credential_id = snapshot.credential_id
                                                     AND date_trunc('hour', later.captured_at AT TIME ZONE 'UTC') = date_trunc('hour', snapshot.captured_at AT TIME ZONE 'UTC')
                                                     AND later.captured_at > snapshot.captured_at)
                                         """;
}