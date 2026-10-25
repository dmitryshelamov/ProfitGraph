using ProfitGraph.Application.Notifications;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Tests.Unit.Domain.Notifications;

/// <summary>
///     Link codes are random, stored only as a hash that ignores case and spaces, and expire after ten minutes; alert texts
///     name the bot by its instrument (ADR 0020).
/// </summary>
public sealed class TelegramLinkCodeTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void CodeIsStoredAsAHashThatIgnoresCaseAndSpaces()
    {
        IssuedTelegramLinkCode issued = TelegramLinkCode.Issue(UserId.New(), Now);

        issued.Code.Length.ShouldBe(10);
        issued.Stored.CodeHash.ShouldNotContain(issued.Code);
        TelegramLinkCode.Hash($"  {issued.Code.ToLowerInvariant()} ").ShouldBe(issued.Stored.CodeHash);
        issued.Stored.IsExpired(Now.AddMinutes(9)).ShouldBeFalse();
        issued.Stored.IsExpired(Now.AddMinutes(10)).ShouldBeTrue();
        TelegramLinkCode.Issue(UserId.New(), Now).Code.ShouldNotBe(issued.Code);
    }

    [Test]
    public void AlertTextNamesTheBotAndTheReason()
    {
        var alert = new TelegramAlertDelivery(BotAlertId.New(), ChatId: 1, BotAlertKind.Degraded, "ForeignOrder", "BTCUSDT", "1h");

        TelegramMessages.Alert(alert).ShouldBe("ProfitGraph: бот BTCUSDT 1h остановлен по безопасности (ForeignOrder).");
    }
}