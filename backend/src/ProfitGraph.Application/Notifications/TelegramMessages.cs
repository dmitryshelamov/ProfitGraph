using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications;

/// <summary>
///     The texts the Telegram bot sends (ADR 0020): replies to <c>/start</c> and alerts by kind and reason. They name the
///     bot by its instrument and timeframe and never carry amounts, keys or venue payloads.
/// </summary>
public static class TelegramMessages
{
    public const string Linked = "ProfitGraph: чат привязан, сюда будут приходить алерты ботов.";
    public const string UnknownCode = "ProfitGraph: код не найден или истёк. Получите новый код в профиле.";
    public const string Help = "ProfitGraph: чтобы получать алерты, отправьте /start и код из профиля.";

    public static string Alert(TelegramAlertDelivery alert)
    {
        var bot = $"{alert.Symbol} {alert.Timeframe}";
        string reason = alert.Reason is null ? string.Empty : $" ({alert.Reason})";

        return alert.Kind switch
        {
            BotAlertKind.Degraded => $"ProfitGraph: бот {bot} остановлен по безопасности{reason}.",
            BotAlertKind.Killed => $"ProfitGraph: бот {bot} аварийно остановлен{reason}.",
            BotAlertKind.RiskRejection => $"ProfitGraph: бот {bot} — заявка отклонена лимитом риска{reason}.",
            BotAlertKind.ExchangeUnavailable => $"ProfitGraph: бот {bot} — биржа не отвечает, бот ждёт.",
            BotAlertKind.GraphChangeRejected => $"ProfitGraph: бот {bot} не принял новую версию стратегии и остался на прежней.",
            _ => $"ProfitGraph: бот {bot} — {alert.Kind}{reason}."
        };
    }
}