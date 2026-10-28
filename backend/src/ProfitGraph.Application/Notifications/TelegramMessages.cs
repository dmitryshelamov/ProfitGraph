using System.Net;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications;

/// <summary>
///     The texts the Telegram bot sends (ADR 0020, ADR 0042), in Russian and HTML parse mode: replies to commands and the
///     notifications, built when they are sent. A bot is named by its name and, without one, by its symbol and timeframe.
///     Every value is HTML-encoded. Keys, tokens and venue payloads are never part of a text.
/// </summary>
public static class TelegramMessages
{
    public const string Linked = "ProfitGraph: чат привязан, сюда будут приходить уведомления ботов.";
    public const string UnknownCode = "ProfitGraph: код не найден или истёк. Получите новый код в профиле.";
    public const string Help = "ProfitGraph: чтобы получать уведомления, отправьте /start и код из профиля.";

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

    public static string Notification(TelegramNotification notification, Bot? bot)
    {
        return notification.Kind switch
        {
            TelegramNotificationKind.Alert => Alert(notification.Details, BotName(bot)),
            _ => throw new ArgumentOutOfRangeException(nameof(notification), notification.Kind, "Unknown notification kind.")
        };
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

    private static string Line(string label, string value)
    {
        return $"{label}: {Encode(value)}";
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}