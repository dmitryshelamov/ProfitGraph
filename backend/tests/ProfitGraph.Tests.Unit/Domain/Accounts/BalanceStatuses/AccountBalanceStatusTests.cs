using ProfitGraph.Domain.Accounts.BalanceStatuses;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Accounts.Snapshots;

namespace ProfitGraph.Tests.Unit.Domain.Accounts.BalanceStatuses;

/// <summary>
///     Balance reads are retried with backoff per credential and a failure never discards the last success (Profit Fox
///     Designer ADR 0069).
/// </summary>
public sealed class AccountBalanceStatusTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void FailuresWaitLongerUpToAnHourAndASuccessResetsThem()
    {
        var status = AccountBalanceStatus.New(ExchangeCredentialId.New());
        status.RecordSuccess(Now);
        List<double> delays = [];

        for (var attempt = 0; attempt < 6; attempt++)
        {
            status.RecordFailure("venue_unavailable", "503", Now);
            delays.Add((status.NextAttemptAt!.Value - Now).TotalMinutes);
        }

        status.LastSuccessAt.ShouldBe(Now);
        delays.ShouldBe([5d, 10d, 20d, 40d, 60d, 60d]);
        status.RecordSuccess(Now.AddHours(1));
        status.ShouldSatisfyAllConditions(
            () => status.ConsecutiveFailures.ShouldBe(0),
            () => status.FailureReason.ShouldBeNull(),
            () => status.NextAttemptAt.ShouldBe(Now.AddHours(1).AddMinutes(5)));
    }

    [Test]
    public void FailureDetailIsShortened()
    {
        var status = AccountBalanceStatus.New(ExchangeCredentialId.New());

        status.RecordFailure("venue_rejected", new string(c: 'x', count: 300), Now);

        status.FailureDetail!.Length.ShouldBe(200);
    }

    [Test]
    public void SnapshotBelongsToItsUtcFiveMinuteBucket()
    {
        AccountSnapshot snapshot = AccountSnapshot.Capture(
            AccountSnapshotId.New(),
            ExchangeCredentialId.New(),
            Now.AddMinutes(7).AddSeconds(42),
            totalValue: 1m,
            "USD",
            availableValue: null,
            []);

        snapshot.BucketStart.ShouldBe(Now.AddMinutes(5));
    }
}