using ProfitGraph.Application.Notifications.Contracts;

namespace ProfitGraph.Application.Notifications.Ports;

/// <summary>
///     The Telegram Bot API (ADR 0020, ADR 0042). It exists only when Telegram is enabled. Telegram's answers to a send are a
///     <see cref="TelegramSendResult" />; a transport failure throws, and the caller tries again.
/// </summary>
public interface ITelegramClient
{
    /// <summary>
    ///     Text messages and button presses from <paramref name="offset" /> on, waiting up to <paramref name="timeout" /> for
    ///     one to arrive.
    /// </summary>
    Task<IReadOnlyList<TelegramUpdateInternal>> GetUpdates(long offset, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    ///     Sends <paramref name="html" /> in HTML parse mode with <paramref name="buttons" /> in one row under it.
    /// </summary>
    Task<TelegramSendResult> SendMessage(long chatId, string html, IReadOnlyList<TelegramButtonInternal> buttons, CancellationToken cancellationToken);

    /// <summary>
    ///     Answers a button press, so the client stops waiting, with a short notice when <paramref name="text" /> is set.
    /// </summary>
    Task AnswerCallback(string callbackId, string? text, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the buttons under one of the bot's messages.
    /// </summary>
    Task RemoveButtons(long chatId, long messageId, CancellationToken cancellationToken);
}