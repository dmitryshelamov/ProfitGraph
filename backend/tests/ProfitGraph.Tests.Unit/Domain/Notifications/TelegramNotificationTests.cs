using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Alerts;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Tests.Unit.Domain.Notifications;

/// <summary>
///     A queued Telegram notification (ADR 0042): it is claimed by one sender at a time, an outcome counts only from the
///     current claim, temporary failures wait 5 s, 15 s, 1, 5 and then 10 minutes or Telegram's longer <c>retry_after</c>,
///     and a notification too old for its kind is skipped.
/// </summary>
public sealed class TelegramNotificationTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 12, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly TelegramClaimId First = TelegramClaimId.New();
    private static readonly TelegramClaimId Second = TelegramClaimId.New();

    [Test]
    public void QueuedNotificationIsDueAtOnce()
    {
        TelegramNotification notification = Alert();

        notification.Status.ShouldBe(TelegramNotificationStatus.Pending);
        notification.IsDue(Now).ShouldBeTrue();
        notification.Attempts.ShouldBe(0);
    }

    [Test]
    public void AlertNamesItsAlertAndOnlyAnAlertDoes()
    {
        Should.Throw<ArgumentException>(() => TelegramNotification.Queue(
            TelegramNotificationId.New(),
            UserId.New(),
            BotId.New(),
            TelegramNotificationKind.Alert,
            alertId: null,
            new Dictionary<string, string>(),
            Now));
    }

    [Test]
    public void ClaimHoldsTheNotificationUntilItExpires()
    {
        TelegramNotification notification = Alert();

        notification.Claim(First, Now).ShouldBeTrue();

        notification.ClaimedUntil.ShouldBe(Now + TelegramNotification.ClaimDuration);
        notification.IsDue(Now.AddSeconds(44)).ShouldBeFalse();
        notification.Claim(Second, Now.AddSeconds(44)).ShouldBeFalse();
        notification.Claim(Second, Now.AddSeconds(45)).ShouldBeTrue();
        notification.ClaimId.ShouldBe(Second);
    }

    [Test]
    public void OutcomeCountsOnlyFromTheCurrentClaim()
    {
        TelegramNotification notification = Alert();
        notification.Claim(First, Now);
        notification.Claim(Second, Now.AddMinutes(1));

        notification.MarkSent(First, Now.AddMinutes(1)).ShouldBeFalse();
        notification.Status.ShouldBe(TelegramNotificationStatus.Pending);
        notification.MarkSent(Second, Now.AddMinutes(1)).ShouldBeTrue();
        notification.Status.ShouldBe(TelegramNotificationStatus.Sent);
        notification.SentAt.ShouldBe(Now.AddMinutes(1));
        notification.Attempts.ShouldBe(1);
        notification.ClaimId.ShouldBeNull();
    }

    [Test]
    public void ExpiredClaimNobodyTookStillRecordsItsOutcome()
    {
        TelegramNotification notification = Alert();
        notification.Claim(First, Now);

        notification.MarkSent(First, Now.AddMinutes(5)).ShouldBeTrue();
    }

    [Test]
    public void FinishedNotificationIsNeverDueAgain()
    {
        TelegramNotification notification = Alert();
        notification.Claim(First, Now);
        notification.MarkSent(First, Now);

        notification.IsDue(Now.AddHours(1)).ShouldBeFalse();
        notification.Claim(Second, Now.AddHours(1)).ShouldBeFalse();
        notification.Fail(First, "http_400", Now).ShouldBeFalse();
    }

    [Test]
    public void TemporaryFailuresWaitLongerEachTimeUpToTenMinutes()
    {
        TelegramNotification notification = Alert();
        DateTimeOffset at = Now;
        List<TimeSpan> waits = [];

        for (var attempt = 0; attempt < 6; attempt++)
        {
            notification.Claim(First, at).ShouldBeTrue();
            notification.RetryLater(First, "http_500", retryAfter: null, at).ShouldBeTrue();
            waits.Add(notification.NextAttemptAt - at);
            at = notification.NextAttemptAt;
        }

        waits.ShouldBe(
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10)
        ]);
        notification.Attempts.ShouldBe(6);
        notification.Status.ShouldBe(TelegramNotificationStatus.Pending);
        notification.LastError.ShouldBe("http_500");
        notification.ClaimId.ShouldBeNull();
        notification.IsDue(at.AddSeconds(-1)).ShouldBeFalse();
        notification.IsDue(at).ShouldBeTrue();
    }

    [Test]
    public void LongerRetryAfterWins()
    {
        TelegramNotification notification = Alert();
        notification.Claim(First, Now);

        notification.RetryLater(First, "http_429", TimeSpan.FromSeconds(30), Now);

        notification.NextAttemptAt.ShouldBe(Now.AddSeconds(30));
    }

    [Test]
    public void ShorterRetryAfterKeepsTheSchedule()
    {
        TelegramNotification notification = Alert();
        notification.Claim(First, Now);

        notification.RetryLater(First, "http_429", TimeSpan.FromSeconds(1), Now);

        notification.NextAttemptAt.ShouldBe(Now.AddSeconds(5));
    }

    [Test]
    public void PermanentRefusalFailsWithAShortError()
    {
        TelegramNotification notification = Alert();
        notification.Claim(First, Now);

        notification.Fail(First, $"http_400: {new string(c: 'x', count: 300)}", Now).ShouldBeTrue();

        notification.Status.ShouldBe(TelegramNotificationStatus.Failed);
        notification.LastError!.Length.ShouldBe(200);
    }

    [Test]
    public void ChatUnavailableFinishesTheNotification()
    {
        TelegramNotification notification = Alert();
        notification.Claim(First, Now);

        notification.MarkChatUnavailable(First, Now).ShouldBeTrue();

        notification.Status.ShouldBe(TelegramNotificationStatus.ChatUnavailable);
    }

    [Test]
    public void AlertIsSkippedAfterADay()
    {
        TelegramNotification notification = Alert();
        notification.Claim(First, Now.AddHours(24));

        notification.Skip(First, Now.AddHours(24)).ShouldBeFalse();
        notification.Skip(First, Now.AddHours(24).AddSeconds(1)).ShouldBeTrue();
        notification.Status.ShouldBe(TelegramNotificationStatus.Skipped);
    }

    private static TelegramNotification Alert()
    {
        return TelegramNotification.Queue(
            TelegramNotificationId.New(),
            UserId.New(),
            BotId.New(),
            TelegramNotificationKind.Alert,
            BotAlertId.New(),
            new Dictionary<string, string> { ["kind"] = nameof(BotAlertKind.Degraded) },
            Now);
    }
}