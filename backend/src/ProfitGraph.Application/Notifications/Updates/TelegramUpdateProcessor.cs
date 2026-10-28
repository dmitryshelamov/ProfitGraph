using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Application.Notifications.Updates;

/// <summary>
///     Hands one Telegram update to its handler (ADR 0042), as Profit Fox does. Updates from bots and from chats that are not
///     private are ignored. A text message goes to the handler of its command, and anything else is answered with the help
///     text; a button press goes to the handler of its action and is always answered, so the client stops waiting.
/// </summary>
public sealed class TelegramUpdateProcessor(
    IEnumerable<ITelegramMessageHandler> messageHandlers,
    IEnumerable<ITelegramCallbackHandler> callbackHandlers,
    ITelegramClient telegram)
{
    private const string PrivateChat = "private";

    public async Task Process(TelegramUpdateInternal update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.Message is { FromBot: false, ChatType: PrivateChat, Text: { } text } message)
        {
            await Handle(message, text, cancellationToken);
        }
        else if (update.Callback is { } callback)
        {
            await Handle(callback, cancellationToken);
        }
    }

    private async Task Handle(TelegramIncomingMessageInternal message, string text, CancellationToken cancellationToken)
    {
        string[] words = text.Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string command = words.Length == 0 ? string.Empty : words[0].Split('@')[0].ToLowerInvariant();

        if (messageHandlers.FirstOrDefault(handler => handler.Command == command) is { } found)
        {
            await found.Handle(message, words[1..], cancellationToken);
        }
        else
        {
            await telegram.SendMessage(message.ChatId, TelegramMessages.Help, [], cancellationToken);
        }
    }

    private async Task Handle(TelegramCallbackInternal callback, CancellationToken cancellationToken)
    {
        string? notice = null;

        try
        {
            if (callback is { FromBot: false, ChatType: PrivateChat, ChatId: { } chatId, Data: { } data })
            {
                int colon = data.IndexOf(value: ':', StringComparison.Ordinal);
                string action = colon < 0 ? data : data[..colon];
                string argument = colon < 0 ? string.Empty : data[(colon + 1)..];
                notice = callbackHandlers.FirstOrDefault(handler => handler.Action == action) is { } found
                    ? await found.Handle(callback, chatId, argument, cancellationToken)
                    : TelegramMessages.UnknownButton;
            }
        }
        finally
        {
            await telegram.AnswerCallback(callback.Id, notice, cancellationToken);
        }
    }
}