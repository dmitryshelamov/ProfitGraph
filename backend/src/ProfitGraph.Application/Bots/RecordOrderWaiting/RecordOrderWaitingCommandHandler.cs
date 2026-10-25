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
        var botId = new BotId(command.BotId);
        DateTimeOffset now = clock.GetUtcNow();
        BotOrderWaiting? current = await waiting.GetForUpdate(botId, command.EngineOrderId, cancellationToken);

        if (command.Code is not { } code)
        {
            if (current is not null)
            {
                current.End(now);
                await waiting.Delete(current, cancellationToken);
            }

            return;
        }

        if (current is null)
        {
            await waiting.Save(BotOrderWaiting.Start(botId, command.EngineOrderId, code, now, command.Until, command.Low, command.High), cancellationToken);
        }
        else if (current.Change(code, now, command.Until, command.Low, command.High))
        {
            await waiting.Save(current, cancellationToken);
        }
    }
}