using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     The value of an exchange account at one moment (Profit Fox Designer ADR 0069): its USD-equivalent total, its
///     available value when the venue reports it, and its coins. A credential has at most one snapshot per UTC
///     five-minute bucket. It includes deposits, withdrawals and price moves; it is not bot profit.
/// </summary>
public sealed class AccountSnapshot : AggregateRoot
{
    public static readonly TimeSpan Bucket = TimeSpan.FromMinutes(5);

    public AccountSnapshotId Id { get; }
    public ExchangeCredentialId CredentialId { get; }
    public DateTimeOffset CapturedAt { get; }
    public decimal TotalValueUsd { get; }
    public decimal? AvailableValueUsd { get; }
    public IReadOnlyList<AccountCoinBalance> Coins { get; }

    /// <summary>
    ///     The start of the UTC five-minute bucket the snapshot belongs to.
    /// </summary>
    public DateTimeOffset BucketStart => new DateTimeOffset(CapturedAt.UtcTicks - CapturedAt.UtcTicks % Bucket.Ticks, TimeSpan.Zero);

    private AccountSnapshot(
        AccountSnapshotId id,
        ExchangeCredentialId credentialId,
        DateTimeOffset capturedAt,
        decimal totalValueUsd,
        decimal? availableValueUsd,
        IReadOnlyList<AccountCoinBalance> coins)
    {
        Id = id;
        CredentialId = credentialId;
        CapturedAt = capturedAt;
        TotalValueUsd = totalValueUsd;
        AvailableValueUsd = availableValueUsd;
        Coins = coins;
    }

    public static AccountSnapshot Capture(
        AccountSnapshotId id,
        ExchangeCredentialId credentialId,
        DateTimeOffset capturedAt,
        decimal totalValueUsd,
        decimal? availableValueUsd,
        IReadOnlyList<AccountCoinBalance> coins)
    {
        return new AccountSnapshot(id, credentialId, capturedAt, totalValueUsd, availableValueUsd, coins);
    }

    public static AccountSnapshot Restore(
        AccountSnapshotId id,
        ExchangeCredentialId credentialId,
        DateTimeOffset capturedAt,
        decimal totalValueUsd,
        decimal? availableValueUsd,
        IReadOnlyList<AccountCoinBalance> coins)
    {
        return new AccountSnapshot(id, credentialId, capturedAt, totalValueUsd, availableValueUsd, coins);
    }
}