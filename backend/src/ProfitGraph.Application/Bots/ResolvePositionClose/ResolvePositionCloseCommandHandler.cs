using MediatR;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Bots.PositionCloses;

namespace ProfitGraph.Application.Bots.ResolvePositionClose;

internal sealed class ResolvePositionCloseCommandHandler(IBotLeaseRepository leases, IBotPositionCloseRepository closes, TimeProvider clock)
    : IRequestHandler<ResolvePositionCloseCommand, bool>
{
    public async Task<bool> Handle(ResolvePositionCloseCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        BotLease? lease = await leases.GetForUpdate(new BotId(command.BotId), cancellationToken);

        if (lease is null || !lease.IsHeldBy(new LeaseHolderId(command.HolderId), command.Epoch, now))
        {
            return false;
        }

        if (await closes.GetForUpdate(new BotPositionCloseId(command.RequestId), cancellationToken) is { Status: BotPositionCloseStatus.Pending } close)
        {
            close.Fail(command.Reason, now);
            await closes.Update(close, cancellationToken);
        }

        return true;
    }
}