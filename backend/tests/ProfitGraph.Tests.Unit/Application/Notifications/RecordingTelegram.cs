using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Tests.Unit.Application.Notifications;

/// <summary>
///     A Telegram client that records messages and callback answers.
/// </summary>
internal sealed class RecordingTelegram : ITelegramClient
{
    public List<(long ChatId, string Text)> Sent { get; } = [];
    public List<(string Id, string? Text)> Answers { get; } = [];

    public Task<IReadOnlyList<TelegramUpdateInternal>> GetUpdates(long offset, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<TelegramUpdateInternal>>([]);
    }

    public Task<TelegramSendResult> SendMessage(long chatId, string html, IReadOnlyList<TelegramButtonInternal> buttons, CancellationToken cancellationToken)
    {
        Sent.Add((chatId, html));

        return Task.FromResult(TelegramSendResult.Sent);
    }

    public Task AnswerCallback(string callbackId, string? text, CancellationToken cancellationToken)
    {
        Answers.Add((callbackId, text));

        return Task.CompletedTask;
    }

    public Task RemoveButtons(long chatId, long messageId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}