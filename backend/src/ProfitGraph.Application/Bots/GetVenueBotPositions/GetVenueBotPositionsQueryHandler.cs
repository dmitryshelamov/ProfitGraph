using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Bots.GetVenueBotPositions;

internal sealed class GetVenueBotPositionsQueryHandler(IBotRepository bots, IBotCheckpointRepository checkpoints)
    : IRequestHandler<GetVenueBotPositionsQuery, IReadOnlyList<VenueBotPositionInternal>>
{
    public async Task<IReadOnlyList<VenueBotPositionInternal>> Handle(GetVenueBotPositionsQuery query, CancellationToken cancellationToken)
    {
        List<VenueBotPositionInternal> positions = [];

        foreach (Bot bot in await bots.ListOnVenue(new VenueId(query.Venue), cancellationToken))
        {
            BotCheckpoint? checkpoint = await checkpoints.Get(bot.Id, cancellationToken);
            positions.Add(new VenueBotPositionInternal(bot.Id.Value, bot.Series.Symbol.Value, checkpoint?.Engine.Account.BaseTotal ?? 0m));
        }

        return positions;
    }
}