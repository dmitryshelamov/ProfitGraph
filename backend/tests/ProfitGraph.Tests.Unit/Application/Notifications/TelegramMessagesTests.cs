using ProfitGraph.Application.Notifications;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Tests.Unit.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Notifications;

/// <summary>
///     Telegram texts (ADR 0042): an alert names the bot by its name, or by its symbol and timeframe without one, says the
///     reason in Russian and encodes every value for HTML.
/// </summary>
public sealed class TelegramMessagesTests
{
    [Test]
    public void AlertNamesTheBotAndTheReason()
    {
        Bot bot = BotFixture.Stopped();
        bot.Rename(new BotName("Лесенка <BTC> & Co"), BotFixture.Created);

        string text = TelegramMessages.Notification(Alert(nameof(BotAlertKind.Degraded), nameof(BotStatusReason.ForeignOrder)), bot);

        text.ShouldBe("⛔ <b>Бот остановлен по безопасности</b>\nБот: Лесенка &lt;BTC&gt; &amp; Co\nПричина: на символе есть чужие заявки");
    }

    [Test]
    public void BotWithoutANameIsNamedBySymbolAndTimeframe()
    {
        string text = TelegramMessages.Notification(Alert(nameof(BotAlertKind.ExchangeUnavailable), reason: null), BotFixture.Stopped());

        text.ShouldBe("📡 <b>Биржа не отвечает</b>\nБот: BTCUSDT 1h\nБот ждёт и спросит биржу снова.");
    }

    [Test]
    public void UnknownReasonIsShownAsItIs()
    {
        string text = TelegramMessages.Notification(Alert(nameof(BotAlertKind.Killed), "Something<new>"), BotFixture.Stopped());

        text.ShouldContain("Причина: Something&lt;new&gt;");
    }

    private static TelegramNotification Alert(string kind, string? reason)
    {
        var details = new Dictionary<string, string> { ["kind"] = kind };

        if (reason is not null)
        {
            details["reason"] = reason;
        }

        return TelegramNotification.Queue(
            TelegramNotificationId.New(),
            UserId.New(),
            BotId.New(),
            TelegramNotificationKind.Alert,
            BotAlertId.New(),
            details,
            BotFixture.Created);
    }
}