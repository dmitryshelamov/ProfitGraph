using MediatR;

using ProfitGraph.Application.Notifications.AcknowledgeTelegramAlert;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Application.Notifications.Updates;

/// <summary>
///     «Принято» under an alert (<c>ack:{alertId}</c>) acknowledges the alert of the chat's user and removes the button
///     (ADR 0042).
/// </summary>
public sealed class AcknowledgeAlertCallbackHandler(ISender sender, ITelegramClient telegram) : ITelegramCallbackHandler
{
    public string Action => TelegramMessages.AcknowledgeAction;

    public async Task<string?> Handle(TelegramCallbackInternal callback, long chatId, string argument, CancellationToken cancellationToken)
    {
        TelegramAcknowledgement result = Guid.TryParse(argument, out Guid alertId)
            ? await sender.Send(new AcknowledgeTelegramAlertCommand(chatId, alertId), cancellationToken)
            : TelegramAcknowledgement.NotFound;

        if (result != TelegramAcknowledgement.NotFound && callback.MessageId is { } messageId)
        {
            await telegram.RemoveButtons(chatId, messageId, cancellationToken);
        }

        return result switch
        {
            TelegramAcknowledgement.Acknowledged => TelegramMessages.Acknowledged,
            TelegramAcknowledgement.AlreadyAcknowledged => TelegramMessages.AlreadyAcknowledged,
            _ => TelegramMessages.AlertNotFound
        };
    }
}