using MediatR;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.RecordOrderWaiting;

/// <summary>
///     Locks the order's wait and changes it. Like every command it runs in one transaction (the transaction behavior), so
///     concurrent reports serialize and the wait's events reach the outbox with it.
/// </summary>
internal sealed class RecordOrderWaitingCommandHandler(IBotOrderWaitingRepository waiting, TimeProvider clock) : IRequestHandler<RecordOrderWaitingCommand>
{
    public async Task Handle(RecordOrderWaitingCommand command, CancellationToken cancellationToken)
    {
        await OrderWaitingRecorder.Record(
            waiting,
            new BotId(command.BotId),
            command.EngineOrderId,
            command.Code,
            clock.GetUtcNow(),
            command.Until,
            command.Low,
            command.High,
            cancellationToken);
    }
}