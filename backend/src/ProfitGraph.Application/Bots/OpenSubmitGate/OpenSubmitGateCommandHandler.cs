using MediatR;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Leases;

namespace ProfitGraph.Application.Bots.OpenSubmitGate;

internal sealed class OpenSubmitGateCommandHandler(IBotLeaseRepository leases, TimeProvider clock) : IRequestHandler<OpenSubmitGateCommand, bool>
{
    public async Task<bool> Handle(OpenSubmitGateCommand command, CancellationToken cancellationToken)
    {
        BotLease? lease = await leases.GetForUpdate(new BotId(command.BotId), cancellationToken);

        if (lease is null || !lease.OpenSubmitGate(new LeaseHolderId(command.HolderId), command.Epoch, clock.GetUtcNow()))
        {
            return false;
        }

        await leases.Update(lease, cancellationToken);

        return true;
    }
}