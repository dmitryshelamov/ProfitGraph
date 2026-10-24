using MediatR;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.RenewBotLease;

internal sealed class RenewBotLeaseCommandHandler(IBotLeaseRepository leases, TimeProvider clock) : IRequestHandler<RenewBotLeaseCommand, bool>
{
    public async Task<bool> Handle(RenewBotLeaseCommand command, CancellationToken cancellationToken)
    {
        BotLease? lease = await leases.GetForUpdate(new BotId(command.BotId), cancellationToken);

        if (lease is null || !lease.Renew(new LeaseHolderId(command.HolderId), command.Epoch, clock.GetUtcNow(), command.Duration))
        {
            return false;
        }

        await leases.Update(lease, cancellationToken);

        return true;
    }
}