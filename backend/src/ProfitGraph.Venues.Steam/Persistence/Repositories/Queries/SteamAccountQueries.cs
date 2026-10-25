namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

internal static class SteamAccountQueries
{
    internal const string Get = """
                                SELECT credential_id AS CredentialId, account_fingerprint AS AccountFingerprint, guard_status AS GuardStatus,
                                       next_check_at AS NextCheckAt, created_at AS CreatedAt, updated_at AS UpdatedAt
                                FROM steam_accounts WHERE credential_id = @credentialId
                                """;

    internal const string Due = """
                                SELECT credential_id AS CredentialId, account_fingerprint AS AccountFingerprint, guard_status AS GuardStatus,
                                       next_check_at AS NextCheckAt, created_at AS CreatedAt, updated_at AS UpdatedAt
                                FROM steam_accounts
                                WHERE guard_status IN ('Checking', 'CoolingDown') AND next_check_at <= @now
                                ORDER BY next_check_at, credential_id
                                """;

    internal const string Record = """
                                   UPDATE steam_accounts
                                   SET guard_status = @status, next_check_at = @nextCheckAt, updated_at = @at
                                   WHERE credential_id = @credentialId AND guard_status = @expected
                                   """;

    internal const string AnyFingerprint = "SELECT EXISTS (SELECT 1 FROM steam_accounts WHERE account_fingerprint = ANY(@fingerprints))";

    internal const string Insert = """
                                   INSERT INTO steam_accounts (credential_id, account_fingerprint, guard_status, next_check_at, created_at, updated_at)
                                   VALUES (@CredentialId, @AccountFingerprint, @GuardStatus, @NextCheckAt, @CreatedAt, @UpdatedAt)
                                   ON CONFLICT (account_fingerprint) DO NOTHING
                                   """;
}