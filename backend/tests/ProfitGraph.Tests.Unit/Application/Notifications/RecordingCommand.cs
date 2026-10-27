using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Updates;

namespace ProfitGraph.Tests.Unit.Application.Notifications;

/// <summary>
///     A <c>/start</c> handler that records its calls.
/// </summary>
internal sealed class RecordingCommand : ITelegramMessageHandler
{
    public List<(long ChatId, string Arguments)> Calls { get; } = [];

    public string Command => "/start";

    public Task Handle(TelegramIncomingMessageInternal message, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Calls.Add((message.ChatId, string.Join(separator: ' ', arguments)));

        return Task.CompletedTask;
    }
}