using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Tests.Unit.Domain.Accounts;

public sealed class AccountSnapshotTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 28, hour: 12, minute: 7, second: 0, TimeSpan.Zero);

    [Test]
    public void SnapshotKeepsItsOwnCopyOfTheCoins()
    {
        var coins = new List<AccountCoinBalance> { new AccountCoinBalance("BTC", Balance: 1m, UsdValue: 100m) };
        AccountSnapshot snapshot = AccountSnapshot.Capture(
            AccountSnapshotId.New(),
            ExchangeCredentialId.New(),
            Now,
            totalValue: 100m,
            "USD",
            availableValue: null,
            coins);

        coins.Add(new AccountCoinBalance("ETH", Balance: 2m, UsdValue: 50m));
        coins[0] = new AccountCoinBalance("BTC", Balance: 0m, UsdValue: 0m);

        snapshot.Coins.ShouldBe([new AccountCoinBalance("BTC", Balance: 1m, UsdValue: 100m)]);
    }

    [Test]
    public void SnapshotBelongsToItsFiveMinuteBucket()
    {
        AccountSnapshot snapshot = AccountSnapshot.Capture(AccountSnapshotId.New(), ExchangeCredentialId.New(), Now, totalValue: 100m, "USD", availableValue: null, []);

        snapshot.BucketStart.ShouldBe(Now.AddMinutes(-2));
    }
}