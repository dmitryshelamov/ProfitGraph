using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.Fills;
using ProfitGraph.Domain.Bots.MarkPrices;
using ProfitGraph.Domain.Bots.Positions;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.GetBotPosition;

/// <summary>
///     The bot's position from its checkpoint. The engine keeps entry fees in the cost basis for strategies; the reported profit
///     takes them out at once (ADR 0063). A long's cost basis is what it paid with its fees, a short's is what it sold for less its
///     fees (ADR 0061), so the average entry price adds the entry fees back for a short and takes them out for a long. A cycle's
///     fees are its fills' fees from its opening through its closing; a cycle opens on its candle's grid, so its range starts after
///     the previous cycle closed and a fill counts in one cycle only.
/// </summary>
internal sealed class GetBotPositionQueryHandler(
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    IBotPositionCycleRepository cycles,
    IBotFillRepository fills,
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

        decimal costBasis = account?.CostBasis ?? 0m;
        decimal entryFees = account?.FeesInCost ?? 0m;
        decimal quantity = Math.Abs(baseTotal);
        DateTimeOffset now = clock.GetUtcNow();
        BotFillRange[] ranges = [.. recent.Select((cycle, index) => CycleRange(cycle, index + 1 < recent.Count ? recent[index + 1] : null, now))];
        IReadOnlyList<decimal> cycleFees = await fills.QuoteFees(bot.Id, ranges, cancellationToken);
        int open = checkpoint?.Engine.PositionOpenedAt is null ? -1 : recent.ToList().FindIndex(cycle => cycle.ClosedAt is null);

        return new BotPositionInternal(
            baseTotal,
            account?.QuoteTotal ?? bot.AllocationCap,
            costBasis,
            entryFees,
            quantity > 0m ? costBasis / quantity : null,
            quantity > 0m ? (baseTotal > 0m ? costBasis - entryFees : costBasis + entryFees) / quantity : null,
            (account?.RealizedPnl ?? 0m) - entryFees,
            checkpoint?.Engine.PositionOpenedAt,
            mark?.Price,
            mark?.At,
            mark?.Source,
            mark is { } valued ? (baseTotal >= 0m ? baseTotal * valued.Price - costBasis : baseTotal * valued.Price + costBasis) + entryFees : null,
            open >= 0 ? cycleFees[open] : null,
            [.. recent.Select((cycle, index) => new BotPositionCycleInternal(cycle.Id.Value, cycle.OpenedAt, cycle.ClosedAt, cycle.RealizedPnl, cycleFees[index]))]);
    }

    private static BotFillRange CycleRange(BotPositionCycle cycle, BotPositionCycle? previous, DateTimeOffset now)
    {
        DateTimeOffset from = previous?.ClosedAt is { } closed && closed >= cycle.OpenedAt ? closed.AddTicks(10) : cycle.OpenedAt;

        return new BotFillRange(from, cycle.ClosedAt ?? now);
    }
}