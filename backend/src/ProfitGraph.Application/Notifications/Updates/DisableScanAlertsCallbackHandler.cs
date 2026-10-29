using MediatR;

using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.DisableScanAlerts;
using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Application.Notifications.Updates;

/// <summary>
///     The «Отключить» button under a scanner alert: switches the preset's alerts off and removes the button.
/// </summary>
public sealed class DisableScanAlertsCallbackHandler(ISender sender, ITelegramClient telegram) : ITelegramCallbackHandler
{
    public string Action => TelegramMessages.DisableScanAlertsAction;

    public async Task<string?> Handle(TelegramCallbackInternal callback, long chatId, string argument, CancellationToken cancellationToken)
    {
        bool disabled = Guid.TryParse(argument, out Guid presetId) && await sender.Send(new DisableScanAlertsCommand(chatId, presetId), cancellationToken);

        if (callback.MessageId is { } messageId)
        {
            await telegram.RemoveButtons(chatId, messageId, cancellationToken);
        }

        return disabled ? TelegramMessages.ScanAlertsDisabled : TelegramMessages.ScanAlertsAlreadyOff;
    }
}