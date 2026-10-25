namespace ProfitGraph.Infrastructure.Accounts.Repositories.Queries;

internal static class AccountSnapshotQueries
{
    internal const string Insert = """
                                   INSERT INTO account_value_snapshots (id, credential_id, captured_at, bucket_start, total_value_usd, available_value_usd, coins)
                                   VALUES (@Id, @CredentialId, @CapturedAt, @BucketStart, @TotalValueUsd, @AvailableValueUsd, CAST(@Coins AS jsonb))
                                   ON CONFLICT (credential_id, bucket_start) DO NOTHING
                                   """;

    internal const string List = """
                                 SELECT id AS Id, credential_id AS CredentialId, captured_at AS CapturedAt, bucket_start AS BucketStart,
                                     total_value_usd AS TotalValueUsd, available_value_usd AS AvailableValueUsd, coins::text AS Coins
                                 FROM account_value_snapshots
                                 WHERE credential_id = @credentialId AND captured_at >= @from AND captured_at < @to
                                 ORDER BY captured_at
                                 LIMIT @limit
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