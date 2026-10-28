using ProfitGraph.Domain.Accounts.BalanceStatuses;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Accounts.Repositories.Converters;

internal static class AccountBalanceStatusDbConverter
{
    public static AccountBalanceStatus ToDomain(this AccountBalanceStatusDb row)
    {
        return AccountBalanceStatus.Restore(
            new ExchangeCredentialId(row.CredentialId),
            row.LastAttemptAt,
            row.LastSuccessAt,
            row.FailureReason,
            row.FailureDetail,
            row.ConsecutiveFailures,
            row.NextAttemptAt);
    }

    public static AccountBalanceStatusDb ToDb(this AccountBalanceStatus status)
    {
        return new AccountBalanceStatusDb
        {
            CredentialId = status.CredentialId.Value,
            LastAttemptAt = status.LastAttemptAt,
            LastSuccessAt = status.LastSuccessAt,
            FailureReason = status.FailureReason,
            FailureDetail = status.FailureDetail,
            ConsecutiveFailures = status.ConsecutiveFailures,
            NextAttemptAt = status.NextAttemptAt
        };
    }
}