using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Accounts.BalanceStatuses;

/// <summary>
///     How reading a credential's account balance went (Profit Fox Designer ADR 0069): the last attempt and success, the
///     last failure's reason and safe detail, and when to read next. A success reads again after five minutes; failures
///     wait 5, 10, 20, 40 and then 60 minutes. A failure never discards the last success.
/// </summary>
public sealed class AccountBalanceStatus : AggregateRoot
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public ExchangeCredentialId CredentialId { get; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public string? FailureReason { get; private set; }
    public string? FailureDetail { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }

    private AccountBalanceStatus(ExchangeCredentialId credentialId)
    {
        CredentialId = credentialId;
    }

    public static AccountBalanceStatus New(ExchangeCredentialId credentialId)
    {
        return new AccountBalanceStatus(credentialId);
    }

    public static AccountBalanceStatus Restore(
        ExchangeCredentialId credentialId,
        DateTimeOffset? lastAttemptAt,
        DateTimeOffset? lastSuccessAt,
        string? failureReason,
        string? failureDetail,
        int consecutiveFailures,
        DateTimeOffset? nextAttemptAt)
    {
        return new AccountBalanceStatus(credentialId)
        {
            LastAttemptAt = lastAttemptAt,
            LastSuccessAt = lastSuccessAt,
            FailureReason = failureReason,
            FailureDetail = failureDetail,
            ConsecutiveFailures = consecutiveFailures,
            NextAttemptAt = nextAttemptAt
        };
    }

    public void RecordSuccess(DateTimeOffset now)
    {
        LastAttemptAt = now;
        LastSuccessAt = now;
        FailureReason = null;
        FailureDetail = null;
        ConsecutiveFailures = 0;
        NextAttemptAt = now + Interval;
    }

    public void RecordFailure(string reason, string? detail, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failure needs a reason code.", nameof(reason));
        }

        LastAttemptAt = now;
        FailureReason = reason;
        FailureDetail = detail is { Length: > 200 } ? detail[..200] : detail;
        ConsecutiveFailures++;
        TimeSpan delay = Interval * Math.Pow(x: 2, Math.Min(ConsecutiveFailures - 1, val2: 10));
        NextAttemptAt = now + (delay < MaxRetryDelay ? delay : MaxRetryDelay);
    }
}