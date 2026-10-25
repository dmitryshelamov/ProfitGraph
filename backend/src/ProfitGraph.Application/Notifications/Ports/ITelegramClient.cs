using ProfitGraph.Application.Notifications.Contracts;

namespace ProfitGraph.Application.Notifications.Ports;

/// <summary>
///     The Telegram Bot API (ADR 0020). It exists only when Telegram is enabled; any failure other than a blocked chat
///     throws, and the caller tries again.
/// </summary>
public interface ITelegramClient
{
    /// <summary>
    ///     Updates from <paramref name="offset" /> on, waiting up to <paramref name="timeout" /> for one to arrive.
    /// </summary>
    Task<IReadOnlyList<TelegramUpdateInternal>> GetUpdates(long offset, TimeSpan timeout, CancellationToken cancellationToken);

    Task<TelegramSendOutcome> SendMessage(long chatId, string text, CancellationToken cancellationToken);
}