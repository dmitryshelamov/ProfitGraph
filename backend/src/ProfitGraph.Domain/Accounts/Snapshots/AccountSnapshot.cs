using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Accounts.Snapshots;

/// <summary>
///     The value of an exchange account at one moment (Profit Fox Designer ADR 0069), in the venue's valuation currency
///     (USD for an exchange, a wallet currency for a marketplace; ADR 0022): its total, its
///     available value when the venue reports it, and its coins. A credential has at most one snapshot per UTC
///     five-minute bucket. It includes deposits, withdrawals and price moves; it is not bot profit.
/// </summary>
public sealed class AccountSnapshot : AggregateRoot
{
    public static readonly TimeSpan Bucket = TimeSpan.FromMinutes(5);

    public AccountSnapshotId Id { get; }
    public ExchangeCredentialId CredentialId { get; }
    public DateTimeOffset CapturedAt { get; }
    public decimal TotalValue { get; }
    public string ValueCurrency { get; }
    public decimal? AvailableValue { get; }
    public IReadOnlyList<AccountCoinBalance> Coins { get; }

    /// <summary>
    ///     The start of the UTC five-minute bucket the snapshot belongs to.
    /// </summary>
    public DateTimeOffset BucketStart => new DateTimeOffset(CapturedAt.UtcTicks - CapturedAt.UtcTicks % Bucket.Ticks, TimeSpan.Zero);

    private AccountSnapshot(
        AccountSnapshotId id,
        ExchangeCredentialId credentialId,
        DateTimeOffset capturedAt,
        decimal totalValue,
        string valueCurrency,
        decimal? availableValue,
        IReadOnlyList<AccountCoinBalance> coins)
    {
        Id = id;
        CredentialId = credentialId;
        CapturedAt = capturedAt;
        TotalValue = totalValue;
        ValueCurrency = valueCurrency is { Length: > 0 and <= 8 } && valueCurrency.All(char.IsAsciiLetterUpper)
            ? valueCurrency
            : throw new ArgumentException("A valuation currency is an upper-case code of up to eight letters.", nameof(valueCurrency));
        AvailableValue = availableValue;
        Coins = coins;
    }

    public static AccountSnapshot Capture(
        AccountSnapshotId id,
        ExchangeCredentialId credentialId,
        DateTimeOffset capturedAt,
        decimal totalValue,
        string valueCurrency,
        decimal? availableValue,
        IReadOnlyList<AccountCoinBalance> coins)
    {
        return new AccountSnapshot(id, credentialId, capturedAt, totalValue, valueCurrency, availableValue, coins);
    }

    public static AccountSnapshot Restore(
        AccountSnapshotId id,
        ExchangeCredentialId credentialId,
        DateTimeOffset capturedAt,
        decimal totalValue,
        string valueCurrency,
        decimal? availableValue,
        IReadOnlyList<AccountCoinBalance> coins)
    {
        return new AccountSnapshot(id, credentialId, capturedAt, totalValue, valueCurrency, availableValue, coins);
    }
}