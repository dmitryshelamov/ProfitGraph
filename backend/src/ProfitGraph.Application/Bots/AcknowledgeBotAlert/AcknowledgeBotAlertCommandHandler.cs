using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.AcknowledgeBotAlert;

internal sealed class AcknowledgeBotAlertCommandHandler(IBotAlertRepository alerts, TimeProvider clock) : IRequestHandler<AcknowledgeBotAlertCommand, BotAlertInternal>
{
    public async Task<BotAlertInternal> Handle(AcknowledgeBotAlertCommand command, CancellationToken cancellationToken)
    {
        BotAlert? alert = command.Id == Guid.Empty ? null : await alerts.GetForUpdate(new BotAlertId(command.Id), cancellationToken);

        if (alert is null || alert.OwnerId != new UserId(command.UserId))
        {
            throw new ResourceNotFoundException("The alert does not exist.", BotErrorCodes.AlertNotFound);
        }

        if (alert.Acknowledge(clock.GetUtcNow()))
        {
            await alerts.Update(alert, cancellationToken);
        }

        return alert.ToInternal();
    }
}