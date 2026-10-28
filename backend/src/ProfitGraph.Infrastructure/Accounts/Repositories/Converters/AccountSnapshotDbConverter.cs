using System.Globalization;
using System.Text.Json;

using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Accounts.Snapshots;
using ProfitGraph.Infrastructure.Accounts.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Accounts.Repositories.Converters;

internal static class AccountSnapshotDbConverter
{
    public static AccountSnapshot ToDomain(this AccountSnapshotDb row)
    {
        AccountCoinBalanceDb[] coins = JsonSerializer.Deserialize<AccountCoinBalanceDb[]>(row.Coins) ?? [];

        return AccountSnapshot.Restore(
            new AccountSnapshotId(row.Id),
            new ExchangeCredentialId(row.CredentialId),
            row.CapturedAt,
            row.TotalValue,
            row.ValueCurrency,
            row.AvailableValue,
            [
                .. coins.Select(coin => new AccountCoinBalance(
                    coin.Coin,
                    decimal.Parse(coin.Balance, CultureInfo.InvariantCulture),
                    coin.UsdValue is null ? null : decimal.Parse(coin.UsdValue, CultureInfo.InvariantCulture)))
            ]);
    }

    public static AccountSnapshotDb ToDb(this AccountSnapshot snapshot)
    {
        return new AccountSnapshotDb
        {
            Id = snapshot.Id.Value,
            CredentialId = snapshot.CredentialId.Value,
            CapturedAt = snapshot.CapturedAt,
            BucketStart = snapshot.BucketStart,
            TotalValue = snapshot.TotalValue,
            ValueCurrency = snapshot.ValueCurrency,
            AvailableValue = snapshot.AvailableValue,
            Coins = JsonSerializer.Serialize(
                snapshot.Coins.Select(coin => new AccountCoinBalanceDb
                {
                    Coin = coin.Coin,
                    Balance = coin.Balance.ToString(CultureInfo.InvariantCulture),
                    UsdValue = coin.UsdValue?.ToString(CultureInfo.InvariantCulture)
                }))
        };
    }
}