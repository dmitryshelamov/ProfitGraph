using ProfitGraph.Application.Notifications.Contracts;

namespace ProfitGraph.Application.Notifications.Ports;

/// <summary>
///     The Telegram Bot API (ADR 0020, ADR 0042). It exists only when Telegram is enabled. Telegram's answers to a send are a
///     <see cref="TelegramSendResult" />; a transport failure throws, and the caller tries again.
/// </summary>
public interface ITelegramClient
{
    /// <summary>
    ///     Updates from <paramref name="offset" /> on, waiting up to <paramref name="timeout" /> for one to arrive.
    /// </summary>
    Task<IReadOnlyList<TelegramUpdateInternal>> GetUpdates(long offset, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    ///     Sends <paramref name="html" /> in HTML parse mode.
    /// </summary>
    Task<TelegramSendResult> SendMessage(long chatId, string html, CancellationToken cancellationToken);
}