using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.Fills;

namespace ProfitGraph.Application.Bots.GetBotFills;

/// <summary>
///     A page of the bot's fills, newest first. A fill whose order's commission the venue has not reported yet is marked
///     pending, from the fee receipts of the bot's current checkpoint (ADR 0023, T13): the fill rows themselves never change.
/// </summary>
internal sealed class GetBotFillsQueryHandler(IBotRepository bots, IBotFillRepository fills, IBotCheckpointRepository checkpoints)
    : IRequestHandler<GetBotFillsQuery, BotFillPageInternal>
{
    public async Task<BotFillPageInternal> Handle(GetBotFillsQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        IReadOnlyList<SequencedBotFill> page = await fills.List(bot.Id, query.Before, query.Limit, cancellationToken);
        BotCheckpoint? checkpoint = await checkpoints.Get(bot.Id, cancellationToken);
        HashSet<long> pending =
        [
            .. checkpoint?.Engine.Account.FeeReceipts
                .Where(receipt => receipt.Slices?.Any(slice => slice.CommissionPending) ?? true)
                .Select(receipt => receipt.OrderId) ?? []
        ];
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
                item.Fill.FilledAt,
                pending.Contains(item.Fill.EngineOrderId)))
        ];

        return new BotFillPageInternal(items, items.Length == query.Limit ? items[^1].Sequence : null);
    }
}