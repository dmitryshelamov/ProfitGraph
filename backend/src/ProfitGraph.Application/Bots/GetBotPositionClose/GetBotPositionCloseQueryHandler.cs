using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.PositionCloses;

namespace ProfitGraph.Application.Bots.GetBotPositionClose;

internal sealed class GetBotPositionCloseQueryHandler(IBotRepository bots, IBotPositionCloseRepository closes)
    : IRequestHandler<GetBotPositionCloseQuery, BotPositionCloseInternal>
{
    public async Task<BotPositionCloseInternal> Handle(GetBotPositionCloseQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);

        return (await closes.GetLatest(bot.Id, cancellationToken))?.ToInternal() ??
            throw new ResourceNotFoundException("The bot has no position close.", PositionCloseRequests.NotFoundCode);
    }
}