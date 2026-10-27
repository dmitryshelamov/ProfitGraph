using MediatR;

using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.LinkTelegramChat;
using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Application.Notifications.Updates;

/// <summary>
///     <c>/start CODE</c> links the chat to the code's user (ADR 0020); <c>/start</c> without a code answers the help text.
/// </summary>
public sealed class StartCommandHandler(ISender sender, ITelegramClient telegram) : ITelegramMessageHandler
{
    public string Command => "/start";

    public async Task Handle(TelegramIncomingMessageInternal message, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (arguments is not [var code, ..])
        {
            await telegram.SendMessage(message.ChatId, TelegramMessages.Help, [], cancellationToken);

            return;
        }

        bool linked = await sender.Send(new LinkTelegramChatCommand(code, message.ChatId), cancellationToken);
        await telegram.SendMessage(message.ChatId, linked ? TelegramMessages.Linked : TelegramMessages.UnknownCode, [], cancellationToken);
    }
}