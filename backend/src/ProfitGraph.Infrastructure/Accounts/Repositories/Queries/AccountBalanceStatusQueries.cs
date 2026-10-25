namespace ProfitGraph.Infrastructure.Accounts.Repositories.Queries;

internal static class AccountBalanceStatusQueries
{
    private const string Columns = """
                                   credential_id AS CredentialId, last_attempt_at AS LastAttemptAt, last_success_at AS LastSuccessAt,
                                       failure_reason AS FailureReason, failure_detail AS FailureDetail, consecutive_failures AS ConsecutiveFailures,
                                       next_attempt_at AS NextAttemptAt
                                   """;

    internal const string Get = $"SELECT {Columns} FROM account_balance_status WHERE credential_id = @credentialId";

    internal const string GetForUpdate = $"SELECT {Columns} FROM account_balance_status WHERE credential_id = @credentialId FOR UPDATE";

    internal const string ListDue = """
                                    SELECT DISTINCT credential.id AS CredentialId, credential.user_id AS OwnerId
                                    FROM exchange_credentials AS credential
                                    JOIN bots AS bot ON bot.exchange_credential_id = credential.id
                                    LEFT JOIN account_balance_status AS status ON status.credential_id = credential.id
                                    WHERE status.next_attempt_at IS NULL OR status.next_attempt_at <= @now
                                    """;

    internal const string Save = """
                                 INSERT INTO account_balance_status (
                                     credential_id, last_attempt_at, last_success_at, failure_reason, failure_detail, consecutive_failures, next_attempt_at)
                                 VALUES (@CredentialId, @LastAttemptAt, @LastSuccessAt, @FailureReason, @FailureDetail, @ConsecutiveFailures, @NextAttemptAt)
                                 ON CONFLICT (credential_id) DO UPDATE SET
                                     last_attempt_at = EXCLUDED.last_attempt_at, last_success_at = EXCLUDED.last_success_at,
                                     failure_reason = EXCLUDED.failure_reason, failure_detail = EXCLUDED.failure_detail,
                                     consecutive_failures = EXCLUDED.consecutive_failures, next_attempt_at = EXCLUDED.next_attempt_at
                                 """;
}