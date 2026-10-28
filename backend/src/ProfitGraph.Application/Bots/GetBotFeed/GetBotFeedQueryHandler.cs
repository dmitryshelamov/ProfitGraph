using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Feed;

namespace ProfitGraph.Application.Bots.GetBotFeed;

internal sealed class GetBotFeedQueryHandler(IBotRepository bots, IBotFeedRepository feed) : IRequestHandler<GetBotFeedQuery, BotFeedPageInternal>
{
    public async Task<BotFeedPageInternal> Handle(GetBotFeedQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        IReadOnlyList<SequencedBotFeedEntry> page = await feed.List(bot.Id, query.Before, query.Limit, cancellationToken);
        BotFeedEntryInternal[] items =
        [
            .. page.Select(item => new BotFeedEntryInternal(item.Sequence, item.Entry.Id.Value, item.Entry.Kind, item.Entry.Details, item.Entry.OccurredAt))
        ];

        return new BotFeedPageInternal(items, items.Length == query.Limit ? items[^1].Sequence : null);
    }
}