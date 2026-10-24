using MediatR;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ReleaseBotLease;

internal sealed class ReleaseBotLeaseCommandHandler(IBotLeaseRepository leases, TimeProvider clock) : IRequestHandler<ReleaseBotLeaseCommand>
{
    public async Task Handle(ReleaseBotLeaseCommand command, CancellationToken cancellationToken)
    {
        BotLease? lease = await leases.GetForUpdate(new BotId(command.BotId), cancellationToken);

        if (lease is not null && lease.Release(new LeaseHolderId(command.HolderId), command.Epoch, clock.GetUtcNow()))
        {
            await leases.Update(lease, cancellationToken);
        }
    }
}