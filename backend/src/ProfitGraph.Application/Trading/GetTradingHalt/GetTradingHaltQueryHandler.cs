using MediatR;

using ProfitGraph.Application.Trading.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Trading.GetTradingHalt;

internal sealed class GetTradingHaltQueryHandler(ITradingHaltRepository halts) : IRequestHandler<GetTradingHaltQuery, TradingHaltInternal>
{
    public async Task<TradingHaltInternal> Handle(GetTradingHaltQuery query, CancellationToken cancellationToken)
    {
        TradingHalt halt = await halts.Get(cancellationToken);

        return new TradingHaltInternal(halt.IsHalted, halt.UpdatedAt);
    }
}