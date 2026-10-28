using MediatR;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ResolveOrderCancel;

internal sealed class ResolveOrderCancelCommandHandler(IBotLeaseRepository leases, IOrderCancelRequestRepository cancels, TimeProvider clock)
    : IRequestHandler<ResolveOrderCancelCommand, bool>
{
    public async Task<bool> Handle(ResolveOrderCancelCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        BotLease? lease = await leases.GetForUpdate(new BotId(command.BotId), cancellationToken);

        if (lease is null || !lease.IsHeldBy(new LeaseHolderId(command.HolderId), command.Epoch, now))
        {
            return false;
        }

        if (await cancels.GetForUpdate(new OrderCancelRequestId(command.RequestId), cancellationToken) is { Status: OrderCancelStatus.Pending } request)
        {
            request.Reject(command.Reason, now);
            await cancels.Update(request, cancellationToken);
        }

        return true;
    }
}