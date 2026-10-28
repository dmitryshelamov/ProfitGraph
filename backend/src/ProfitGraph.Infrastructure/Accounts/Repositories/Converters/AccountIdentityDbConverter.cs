using ProfitGraph.Domain.Accounts;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Accounts.Repositories.Converters;

internal static class AccountIdentityDbConverter
{
    public static AccountIdentity ToDomain(this AccountIdentityDb row)
    {
        return AccountIdentity.Restore(
            new ExchangeCredentialId(row.CredentialId),
            row.ReadAt is null
                ? null
                : new AccountIdentityDetails(row.AccountId, row.AccountName, row.AccountKind, row.AccessLevel, row.ReadOnly, row.Permissions, row.ExpiresAt),
            row.ReadAt,
            row.LastAttemptAt,
            row.FailureReason,
            row.FailureDetail,
            row.NextAttemptAt);
    }

    public static AccountIdentityDb ToDb(this AccountIdentity identity)
    {
        AccountIdentityDetails? details = identity.Details;

        return new AccountIdentityDb
        {
            CredentialId = identity.CredentialId.Value,
            AccountId = details?.AccountId,
            AccountName = details?.Name,
            AccountKind = details?.Kind,
            AccessLevel = details?.AccessLevel,
            ReadOnly = details?.ReadOnly,
            Permissions = [.. details?.Permissions ?? []],
            ExpiresAt = details?.ExpiresAt,
            ReadAt = identity.ReadAt,
            LastAttemptAt = identity.LastAttemptAt,
            FailureReason = identity.FailureReason,
            FailureDetail = identity.FailureDetail,
            NextAttemptAt = identity.NextAttemptAt
        };
    }
}