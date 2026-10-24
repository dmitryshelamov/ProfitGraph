using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.GetBot;

internal sealed class GetBotQueryHandler(IBotRepository bots) : IRequestHandler<GetBotQuery, BotInternal>
{
    public async Task<BotInternal> Handle(GetBotQuery query, CancellationToken cancellationToken)
    {
        return (await bots.GetOwned(query.Id, query.OwnerId, cancellationToken)).ToInternal();
    }
}