using MediatR;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Bots.ManualOrders;

namespace ProfitGraph.Application.Bots.ResolveManualOrder;

internal sealed class ResolveManualOrderCommandHandler(IBotLeaseRepository leases, IManualOrderRequestRepository manualOrders, TimeProvider clock)
    : IRequestHandler<ResolveManualOrderCommand, bool>
{
    public async Task<bool> Handle(ResolveManualOrderCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        BotLease? lease = await leases.GetForUpdate(new BotId(command.BotId), cancellationToken);

        if (lease is null || !lease.IsHeldBy(new LeaseHolderId(command.HolderId), command.Epoch, now))
        {
            return false;
        }

        if (await manualOrders.GetForUpdate(new ManualOrderRequestId(command.RequestId), cancellationToken) is { Status: ManualOrderStatus.Pending } request)
        {
            request.Reject(command.Reason, now);
            await manualOrders.Update(request, cancellationToken);
        }

        return true;
    }
}