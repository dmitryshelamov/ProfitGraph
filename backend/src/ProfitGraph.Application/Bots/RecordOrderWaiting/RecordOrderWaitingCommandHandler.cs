using MediatR;

using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.RecordOrderWaiting;

/// <summary>
///     Locks the order's wait and changes it in one transaction, so concurrent reports serialize and their events reach the
///     outbox.
/// </summary>
internal sealed class RecordOrderWaitingCommandHandler(IBotOrderWaitingRepository waiting, ITransactionExecutor transactions, TimeProvider clock)
    : IRequestHandler<RecordOrderWaitingCommand>
{
    public async Task Handle(RecordOrderWaitingCommand command, CancellationToken cancellationToken)
    {
        await transactions.ExecuteAsync(
            async () =>
            {
                await Record(command, cancellationToken);

                return true;
            },
            cancellationToken);
    }

    private async Task Record(RecordOrderWaitingCommand command, CancellationToken cancellationToken)
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