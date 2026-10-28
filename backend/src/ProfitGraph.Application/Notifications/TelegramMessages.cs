using System.Globalization;
using System.Net;

using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Notifications;

/// <summary>
///     The texts the Telegram bot sends (ADR 0020, ADR 0042), in Russian and HTML parse mode: replies to commands and the
///     notifications, built when they are sent. Amounts are allowed (ADR 0042). A bot is named by its name and, without one, by its symbol and timeframe.
///     Every value is HTML-encoded. Keys, tokens and venue payloads are never part of a text.
/// </summary>
public static class TelegramMessages
{
    public const string Linked = "ProfitGraph: чат привязан, сюда будут приходить уведомления ботов.";
    public const string UnknownCode = "ProfitGraph: код не найден или истёк. Получите новый код в профиле.";

    public const string Help =
        "ProfitGraph: чтобы получать уведомления, отправьте /start и код из профиля. Кнопка «Принято» под алертом отмечает его прочитанным.";

    public const string AcknowledgeAction = "ack";
    public const string Acknowledged = "Принято";
    public const string AlreadyAcknowledged = "Алерт уже принят";
    public const string AlertNotFound = "Алерт не найден";
    public const string UnknownButton = "Эта кнопка больше не работает";

    private static readonly Dictionary<string, string> ReasonTexts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(BotStatusReason.RuntimeFailure)] = "сбой выполнения, бот перезапустится",
        [nameof(BotStatusReason.NoGateway)] = "торговля в этом процессе выключена",
        [nameof(BotStatusReason.UnknownVenue)] = "неизвестная биржа",
        [nameof(BotStatusReason.IntentFailed)] = "заявку не удалось доставить",
        [nameof(BotStatusReason.UnresolvedOrder)] = "состояние заявки на бирже не установлено",
        [nameof(BotStatusReason.ForeignOrder)] = "на символе есть чужие заявки",
        [nameof(BotStatusReason.KeyPermissions)] = "у ключа нет нужных прав",
        [nameof(BotStatusReason.UnsupportedFeeAsset)] = "комиссия в неподдерживаемой валюте",
        [nameof(BotStatusReason.InsufficientBalance)] = "недостаточно средств",
        [nameof(BotStatusReason.HistoryDataCorrection)] = "площадка исправила историю свечей, бот ждёт владельца",
        [nameof(BotStatusReason.VenueAccountProtection)] = "аккаунт площадки под защитой, сначала проверьте его",
        [nameof(BotStatusReason.Killed)] = "аварийная остановка",
        [nameof(BotStatusReason.DailyLossLimit)] = "достигнут дневной лимит убытка",
        ["RiskLimit"] = "лимит риска"
    };

    /// <summary>
    ///     The bot's name for people: its name, or its symbol and timeframe.
    /// </summary>
    public static string BotName(Bot? bot)
    {
        return bot is null ? "без имени" : bot.Name?.Value ?? $"{bot.Series.Symbol} {bot.Series.Timeframe.Key}";
    }

    /// <summary>
    ///     The buttons under a notification: «Принято» under an alert, nothing under anything else.
    /// </summary>
    public static IReadOnlyList<TelegramButtonInternal> Buttons(TelegramNotification notification)
    {
        return notification.AlertId is { } alertId ? [new TelegramButtonInternal(Acknowledged, $"{AcknowledgeAction}:{alertId.Value:D}")] : [];
    }

    /// <summary>
    ///     The text of a notification; <paramref name="venue" /> names the bot's venue in a filled-order message.
    /// </summary>
    public static string Notification(TelegramNotification notification, Bot? bot, IVenueDescriptor? venue)
    {
        ArgumentNullException.ThrowIfNull(notification);
        string name = BotName(bot);

        return notification.Kind switch
        {
            TelegramNotificationKind.Alert => Alert(notification.Details, name),
            TelegramNotificationKind.Started => Lines("▶️ <b>Бот запущен</b>", Line("Бот", name)),
            TelegramNotificationKind.StopRequested => Lines("⏸ <b>Бот останавливается по команде владельца</b>", Line("Бот", name), "Заявки на бирже остаются."),
            TelegramNotificationKind.Stopped => Lines("⏹ <b>Бот остановлен</b>", Line("Бот", name)),
            TelegramNotificationKind.Recovered => Lines("🔄 <b>Бот снова работает</b>", Line("Бот", name)),
            TelegramNotificationKind.WaitingForOwner => Lines(
                "🛑 <b>Бот ждёт владельца</b>",
                Line("Бот", name),
                "Автоматические перезапуски исчерпаны, запустите бота вручную."),
            TelegramNotificationKind.PositionClosed => PositionClosed(notification.Details, name),
            TelegramNotificationKind.OrderFilled => OrderFilled(notification.Details, bot, name, venue),
            _ => throw new ArgumentOutOfRangeException(nameof(notification), notification.Kind, "Unknown notification kind.")
        };
    }

    /// <summary>
    ///     Profit Fox's filled-order message: the side and symbol, the bot, venue and market, the quantity at the lot step, the
    ///     average price at the tick, the amount at the quote precision, the fee when there is one, a sale's profit, and hashtags.
    /// </summary>
    private static string OrderFilled(IReadOnlyDictionary<string, string> details, Bot? bot, string name, IVenueDescriptor? venue)
    {
        bool buy = details.GetValueOrDefault("side") == nameof(OrderSide.Buy);
        decimal grossBase = Number(details, "grossBase");
        decimal grossQuote = Number(details, "grossQuote");
        decimal fee = Number(details, "fee");
        int quotePrecision = int.Parse(details["quotePrecision"], CultureInfo.InvariantCulture);
        decimal? realized = details.TryGetValue("realizedPnl", out string? profit) ? decimal.Parse(profit, CultureInfo.InvariantCulture) : null;
        string symbol = bot?.Series.Symbol.Value ?? string.Empty;
        string venueName = venue?.Name ?? bot?.Series.Venue.Value ?? string.Empty;
        string icon = buy ? "✅" : realized > 0m ? "🤑" : "💱";
        List<string> lines = [$"{icon} <b>{(buy ? "КУПЛЕНО" : "ПРОДАНО")} · {Encode(symbol)}</b>", Line("Бот", name), Line("Биржа", venueName)];

        if (venue?.Market is { } market)
        {
            lines.Add(Line("Рынок", market));
        }

        lines.Add(Line("Количество", Fixed(grossBase, Scale(Number(details, "quantityStep")))));
        lines.Add(Line("Средняя цена исполнения", Fixed(grossQuote / grossBase, Scale(Number(details, "priceTick")))));
        lines.Add(Line(buy ? "Потрачено" : "Получено", Fixed(grossQuote, quotePrecision)));

        if (fee != 0m)
        {
            string asset = details.GetValueOrDefault("feeAsset") == nameof(FeeAsset.Base) ? " (в базовом активе)" : string.Empty;
            lines.Add(Line(fee > 0m ? "Комиссия" : "Возврат комиссии", ExactDecimalText.Format(Math.Abs(fee)) + asset));
        }

        if (realized is { } pnl)
        {
            lines.Add(Line(pnl >= 0m ? "Прибыль (PnL)" : "Убыток (PnL)", Signed(Fixed(pnl, quotePrecision), pnl)));
        }

        string tags = string.Join(separator: ' ', new[] { symbol, venueName }.Select(Hashtag).Where(tag => tag.Length > 1));

        if (tags.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add(tags);
        }

        return string.Join(separator: '\n', lines);
    }

    private static string PositionClosed(IReadOnlyDictionary<string, string> details, string name)
    {
        decimal pnl = Number(details, "realizedPnl");

        return Lines(
            pnl >= 0m ? "💰 <b>Позиция закрыта</b>" : "📉 <b>Позиция закрыта</b>",
            Line("Бот", name),
            Line(pnl >= 0m ? "Прибыль (PnL)" : "Убыток (PnL)", Signed(ExactDecimalText.Format(pnl), pnl)));
    }

    private static string Alert(IReadOnlyDictionary<string, string> details, string botName)
    {
        string kind = details.GetValueOrDefault("kind") ?? string.Empty;
        string title = kind switch
        {
            nameof(BotAlertKind.Degraded) => "⛔ <b>Бот остановлен по безопасности</b>",
            nameof(BotAlertKind.Killed) => "🛑 <b>Аварийная остановка</b>",
            nameof(BotAlertKind.RiskRejection) => "🚫 <b>Заявка отклонена лимитом риска</b>",
            nameof(BotAlertKind.ExchangeUnavailable) => "📡 <b>Биржа не отвечает</b>",
            nameof(BotAlertKind.GraphChangeRejected) => "⚠️ <b>Версия стратегии не принята</b>",
            _ => $"⚠️ <b>Алерт {Encode(kind)}</b>"
        };
        string? note = kind switch
        {
            nameof(BotAlertKind.ExchangeUnavailable) => "Бот ждёт и спросит биржу снова.",
            nameof(BotAlertKind.GraphChangeRejected) => "Бот остался на прежней версии.",
            _ => null
        };
        List<string> lines = [title, Line("Бот", botName)];

        if (details.GetValueOrDefault("reason") is { } reason)
        {
            lines.Add(Line("Причина", ReasonTexts.GetValueOrDefault(reason) ?? reason));
        }

        if (note is not null)
        {
            lines.Add(note);
        }

        return string.Join(separator: '\n', lines);
    }

    private static string Lines(params string[] lines)
    {
        return string.Join(separator: '\n', lines);
    }

    private static decimal Number(IReadOnlyDictionary<string, string> details, string key)
    {
        return decimal.Parse(details[key], NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     The number of decimal places of a step or tick: 0.001 has three, 0.5 one and 10 none.
    /// </summary>
    private static int Scale(decimal step)
    {
        string text = ExactDecimalText.Format(step);
        int dot = text.IndexOf(value: '.', StringComparison.Ordinal);

        return dot < 0 ? 0 : text.Length - dot - 1;
    }

    private static string Fixed(decimal value, int decimals)
    {
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    private static string Signed(string text, decimal value)
    {
        return value >= 0m ? $"+{text}" : text;
    }

    /// <summary>
    ///     A hashtag keeps letters, digits and underscores only.
    /// </summary>
    private static string Hashtag(string text)
    {
        return "#" + new string([.. text.Where(character => char.IsLetterOrDigit(character) || character == '_')]);
    }

    private static string Line(string label, string value)
    {
        return $"{label}: {Encode(value)}";
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}