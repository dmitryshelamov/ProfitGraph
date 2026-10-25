using MediatR;

using ProfitGraph.Application.Trading.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Trading.ResumeTrading;

internal sealed class ResumeTradingCommandHandler(ITradingHaltRepository halts, TimeProvider clock) : IRequestHandler<ResumeTradingCommand, TradingHaltInternal>
{
    public async Task<TradingHaltInternal> Handle(ResumeTradingCommand command, CancellationToken cancellationToken)
    {
        TradingHalt halt = await halts.GetForUpdate(cancellationToken);

        if (halt.Resume(clock.GetUtcNow()))
        {
            await halts.Update(halt, cancellationToken);
        }

        return new TradingHaltInternal(halt.IsHalted, halt.UpdatedAt);
    }
}