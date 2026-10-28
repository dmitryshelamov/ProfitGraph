namespace ProfitGraph.Infrastructure.Accounts.Repositories.Queries;

internal static class AccountIdentityQueries
{
    private const string Columns = """
                                   credential_id AS CredentialId, account_id AS AccountId, account_name AS AccountName, account_kind AS AccountKind,
                                       access_level AS AccessLevel, read_only AS ReadOnly, permissions AS Permissions, expires_at AS ExpiresAt, read_at AS ReadAt,
                                       last_attempt_at AS LastAttemptAt, failure_reason AS FailureReason, failure_detail AS FailureDetail, next_attempt_at AS NextAttemptAt
                                   """;

    internal const string Get = $"SELECT {Columns} FROM account_identities WHERE credential_id = @credentialId";

    internal const string GetForUpdate = $"SELECT {Columns} FROM account_identities WHERE credential_id = @credentialId FOR UPDATE";

    internal const string ListDue = """
                                    SELECT DISTINCT credential.id AS CredentialId, credential.user_id AS OwnerId
                                    FROM exchange_credentials AS credential
                                    JOIN bots AS bot ON bot.exchange_credential_id = credential.id
                                    LEFT JOIN account_identities AS identity ON identity.credential_id = credential.id
                                    WHERE credential.venue = ANY(@venues) AND (identity.next_attempt_at IS NULL OR identity.next_attempt_at <= @now)
                                    """;

    internal const string Save = """
                                 INSERT INTO account_identities (
                                     credential_id, account_id, account_name, account_kind, access_level, read_only, permissions, expires_at, read_at,
                                     last_attempt_at, failure_reason, failure_detail, next_attempt_at)
                                 VALUES (
                                     @CredentialId, @AccountId, @AccountName, @AccountKind, @AccessLevel, @ReadOnly, @Permissions, @ExpiresAt, @ReadAt,
                                     @LastAttemptAt, @FailureReason, @FailureDetail, @NextAttemptAt)
                                 ON CONFLICT (credential_id) DO UPDATE SET
                                     account_id = EXCLUDED.account_id, account_name = EXCLUDED.account_name, account_kind = EXCLUDED.account_kind,
                                     access_level = EXCLUDED.access_level, read_only = EXCLUDED.read_only, permissions = EXCLUDED.permissions,
                                     expires_at = EXCLUDED.expires_at, read_at = EXCLUDED.read_at, last_attempt_at = EXCLUDED.last_attempt_at,
                                     failure_reason = EXCLUDED.failure_reason, failure_detail = EXCLUDED.failure_detail, next_attempt_at = EXCLUDED.next_attempt_at
                                 """;
}