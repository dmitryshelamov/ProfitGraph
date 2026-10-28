using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     The venue account a credential trades on as the venue last described it (ADR 0049), cached so a page never reads the
///     venue: the details of the last successful read, when it was, and how the last attempt went. A success is read again
///     after <see cref="RefreshInterval" />, a failure after <see cref="RetryDelay" />; a failure never discards the last
///     details.
/// </summary>
public sealed class AccountIdentity : AggregateRoot
{
    private const int MaxDetailLength = 200;
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    public ExchangeCredentialId CredentialId { get; }

    /// <summary>
    ///     The details of the last successful read, or null before one.
    /// </summary>
    public AccountIdentityDetails? Details { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public string? FailureDetail { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }

    private AccountIdentity(ExchangeCredentialId credentialId)
    {
        CredentialId = credentialId;
    }

    public static AccountIdentity New(ExchangeCredentialId credentialId)
    {
        return new AccountIdentity(credentialId);
    }

    public static AccountIdentity Restore(
        ExchangeCredentialId credentialId,
        AccountIdentityDetails? details,
        DateTimeOffset? readAt,
        DateTimeOffset? lastAttemptAt,
        string? failureReason,
        string? failureDetail,
        DateTimeOffset? nextAttemptAt)
    {
        if (details is null != readAt is null)
        {
            throw new ArgumentException("Stored account details need the time they were read.", nameof(details));
        }

        return new AccountIdentity(credentialId)
        {
            Details = details,
            ReadAt = readAt,
            LastAttemptAt = lastAttemptAt,
            FailureReason = failureReason,
            FailureDetail = failureDetail,
            NextAttemptAt = nextAttemptAt
        };
    }

    public void RecordRead(AccountIdentityDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        Details = details;
        ReadAt = now;
        LastAttemptAt = now;
        FailureReason = null;
        FailureDetail = null;
        NextAttemptAt = now + RefreshInterval;
    }

    public void RecordFailure(string reason, string? detail, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failure needs a reason code.", nameof(reason));
        }

        LastAttemptAt = now;
        FailureReason = reason;
        FailureDetail = detail is { Length: > MaxDetailLength } ? detail[..MaxDetailLength] : detail;
        NextAttemptAt = now + RetryDelay;
    }
}