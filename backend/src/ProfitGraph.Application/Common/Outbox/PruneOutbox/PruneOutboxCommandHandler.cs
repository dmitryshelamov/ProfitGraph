using MediatR;

namespace ProfitGraph.Application.Common.Outbox.PruneOutbox;

internal sealed class PruneOutboxCommandHandler(IOutboxStore outbox, TimeProvider clock) : IRequestHandler<PruneOutboxCommand, int>
{
    public Task<int> Handle(PruneOutboxCommand command, CancellationToken cancellationToken)
    {
        return outbox.DeleteDelivered(clock.GetUtcNow() - command.Retention, command.BatchSize, cancellationToken);
    }
}