using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Tests.Unit.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Notifications;

/// <summary>
///     Telegram texts (ADR 0042): an alert names the bot by its name, or by its symbol and timeframe without one, says the
///     reason in Russian, encodes every value for HTML and carries «Принято»; a filled order follows Profit Fox's message with
///     the quantity at the lot step, the price at the tick and amounts at the quote precision; lifecycle texts name the bot.
/// </summary>
public sealed class TelegramMessagesTests
{
    [Test]
    public void AlertNamesTheBotAndTheReason()
    {
        Bot bot = BotFixture.Stopped();
        bot.Rename(new BotName("Лесенка <BTC> & Co"), BotFixture.Created);

        string text = TelegramMessages.Notification(Alert(nameof(BotAlertKind.Degraded), nameof(BotStatusReason.ForeignOrder)), bot, venue: null);

        text.ShouldBe("⛔ <b>Бот остановлен по безопасности</b>\nБот: Лесенка &lt;BTC&gt; &amp; Co\nПричина: на символе есть чужие заявки");
    }

    [Test]
    public void BotWithoutANameIsNamedBySymbolAndTimeframe()
    {
        string text = TelegramMessages.Notification(Alert(nameof(BotAlertKind.ExchangeUnavailable), reason: null), BotFixture.Stopped(), venue: null);

        text.ShouldBe("📡 <b>Биржа не отвечает</b>\nБот: BTCUSDT 1h\nБот ждёт и спросит биржу снова.");
    }

    [Test]
    public void UnknownReasonIsShownAsItIs()
    {
        string text = TelegramMessages.Notification(Alert(nameof(BotAlertKind.Killed), "Something<new>"), BotFixture.Stopped(), venue: null);

        text.ShouldContain("Причина: Something&lt;new&gt;");
    }

    [Test]
    public void FilledBuyFollowsProfitFox()
    {
        string text = TelegramMessages.Notification(
            Filled(nameof(OrderSide.Buy), "0.0015", "97.5", "0.0000015", nameof(FeeAsset.Base), realizedPnl: null),
            BotFixture.Stopped(),
            new StubVenueDescriptor("Bybit", "Spot"));

        text.ShouldBe(
            """
                ✅ <b>КУПЛЕНО · BTCUSDT</b>
                Бот: BTCUSDT 1h
                Биржа: Bybit
                Рынок: Spot
                Количество: 0.001500
                Средняя цена исполнения: 65000.00
                Потрачено: 97.50000000
                Комиссия: 0.0000015 (в базовом активе)

                #BTCUSDT #Bybit
                """.ReplaceLineEndings("\n"));
    }

    [Test]
    public void SaleWithProfitShowsItAndTheReceivedAmount()
    {
        string text = TelegramMessages.Notification(
            Filled(nameof(OrderSide.Sell), "0.0015", "99", "0.099", nameof(FeeAsset.Quote), "1.40100000"),
            BotFixture.Stopped(),
            new StubVenueDescriptor("Bybit", "Spot"));

        text.ShouldStartWith("🤑 <b>ПРОДАНО · BTCUSDT</b>");
        text.ShouldContain("\nПолучено: 99.00000000\nКомиссия: 0.099\nПрибыль (PnL): +1.40100000\n");
    }

    [Test]
    public void SaleWithLossAndNoFeeOrMarket()
    {
        string text = TelegramMessages.Notification(
            Filled(nameof(OrderSide.Sell), "2", "190", "0", nameof(FeeAsset.Quote), "-10.5"),
            BotFixture.Stopped(),
            new StubVenueDescriptor("Steam Community Market", market: null));

        text.ShouldStartWith("💱 <b>ПРОДАНО · BTCUSDT</b>");
        text.ShouldNotContain("Рынок");
        text.ShouldNotContain("Комиссия");
        text.ShouldContain("Убыток (PnL): -10.50000000");
        text.ShouldEndWith("#BTCUSDT #SteamCommunityMarket");
    }

    [Test]
    public void ClosedPositionShowsTheCyclesProfit()
    {
        TelegramNotification closed = Notification(TelegramNotificationKind.PositionClosed, new Dictionary<string, string> { ["realizedPnl"] = "3.25" });

        TelegramMessages.Notification(closed, BotFixture.Stopped(), venue: null).ShouldBe("💰 <b>Позиция закрыта</b>\nБот: BTCUSDT 1h\nПрибыль (PnL): +3.25");
    }

    [Test]
    public void LifecycleTextsNameTheBot()
    {
        Bot bot = BotFixture.Stopped();
        var empty = new Dictionary<string, string>();

        TelegramMessages.Notification(Notification(TelegramNotificationKind.Started, empty), bot, venue: null).ShouldBe("▶️ <b>Бот запущен</b>\nБот: BTCUSDT 1h");
        TelegramMessages.Notification(Notification(TelegramNotificationKind.WaitingForOwner, empty), bot, venue: null)
            .ShouldBe("🛑 <b>Бот ждёт владельца</b>\nБот: BTCUSDT 1h\nАвтоматические перезапуски исчерпаны, запустите бота вручную.");
        TelegramMessages.Buttons(Notification(TelegramNotificationKind.Stopped, empty)).ShouldBeEmpty();
    }

    [Test]
    public void AlertCarriesTheAcceptButton()
    {
        TelegramNotification alert = Alert(nameof(BotAlertKind.Killed), reason: null);

        TelegramMessages.Buttons(alert).ShouldBe([new TelegramButtonInternal("Принято", $"ack:{alert.AlertId!.Value.Value:D}")]);
    }

    private static TelegramNotification Filled(string side, string grossBase, string grossQuote, string fee, string feeAsset, string? realizedPnl)
    {
        var details = new Dictionary<string, string>
        {
            ["side"] = side,
            ["grossBase"] = grossBase,
            ["grossQuote"] = grossQuote,
            ["fee"] = fee,
            ["feeAsset"] = feeAsset,
            ["quantityStep"] = "0.000001",
            ["priceTick"] = "0.01",
            ["quotePrecision"] = "8"
        };

        if (realizedPnl is not null)
        {
            details["realizedPnl"] = realizedPnl;
        }

        return Notification(TelegramNotificationKind.OrderFilled, details);
    }

    private static TelegramNotification Notification(TelegramNotificationKind kind, IReadOnlyDictionary<string, string> details)
    {
        return TelegramNotification.Queue(TelegramNotificationId.New(), UserId.New(), BotId.New(), kind, alertId: null, details, BotFixture.Created);
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