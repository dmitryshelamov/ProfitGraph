using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.GetBotFills;

internal sealed class GetBotFillsQueryHandler(IBotRepository bots, IBotFillRepository fills) : IRequestHandler<GetBotFillsQuery, BotFillPageInternal>
{
    public async Task<BotFillPageInternal> Handle(GetBotFillsQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        IReadOnlyList<SequencedBotFill> page = await fills.List(bot.Id, query.Before, query.Limit, cancellationToken);
        BotFillInternal[] items =
        [
            .. page.Select(item => new BotFillInternal(
                item.Sequence,
                item.Fill.Id.Value,
                item.Fill.EngineOrderId,
                item.Fill.Side,
                item.Fill.Tag,
                item.Fill.GrossBase,
                item.Fill.GrossQuote,
                item.Fill.Fee,
                item.Fill.FeeAsset,
                item.Fill.Price,
                item.Fill.FilledAt))
        ];

        return new BotFillPageInternal(items, items.Length == query.Limit ? items[^1].Sequence : null);
    }
}