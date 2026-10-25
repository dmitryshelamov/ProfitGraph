using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.GetBotPosition;

internal sealed class GetBotPositionQueryHandler(
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    IBotPositionCycleRepository cycles,
    ILiveMarkPriceRepository prices,
    TimeProvider clock)
    : IRequestHandler<GetBotPositionQuery, BotPositionInternal>
{
    public async Task<BotPositionInternal> Handle(GetBotPositionQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        BotCheckpoint? checkpoint = await checkpoints.Get(bot.Id, cancellationToken);
        ExternalAccountCheckpoint? account = checkpoint?.Engine.Account;
        IReadOnlyList<BotPositionCycle> recent = await cycles.List(bot.Id, query.CycleLimit, cancellationToken);
        decimal baseTotal = account?.BaseTotal ?? 0m;
        (decimal Price, DateTimeOffset At, MarkPriceSource Source)? mark = null;

        if (checkpoint is not null)
        {
            LiveMarkPrice? ticker = await prices.Get(bot.Series.Venue, bot.Series.Symbol, cancellationToken);
            mark = ticker is not null && ticker.Displaces(checkpoint.Engine.LastCandleCloseTime, clock.GetUtcNow())
                ? (ticker.Price, ticker.ObservedAt, MarkPriceSource.Ticker)
                : (checkpoint.Engine.LastClosedCandle.Close, checkpoint.Engine.LastCandleCloseTime, MarkPriceSource.Candle);
        }

        return new BotPositionInternal(
            baseTotal,
            account?.QuoteTotal ?? bot.AllocationCap,
            account?.CostBasis ?? 0m,
            baseTotal > 0m ? account!.CostBasis / baseTotal : null,
            account?.RealizedPnl ?? 0m,
            checkpoint?.Engine.PositionOpenedAt,
            mark?.Price,
            mark?.At,
            mark?.Source,
            mark is { } valued ? baseTotal * valued.Price - (account?.CostBasis ?? 0m) : null,
            [.. recent.Select(cycle => new BotPositionCycleInternal(cycle.Id.Value, cycle.OpenedAt, cycle.ClosedAt, cycle.RealizedPnl))]);
    }
}