using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Application.Notifications.Updates;

/// <summary>
///     <c>/help</c> answers what the bot does.
/// </summary>
public sealed class HelpCommandHandler(ITelegramClient telegram) : ITelegramMessageHandler
{
    public string Command => "/help";

    public Task Handle(TelegramIncomingMessageInternal message, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        return telegram.SendMessage(message.ChatId, TelegramMessages.Help, [], cancellationToken);
    }
}