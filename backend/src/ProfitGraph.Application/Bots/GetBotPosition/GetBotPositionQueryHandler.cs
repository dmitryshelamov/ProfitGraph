using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.GetBotPosition;

internal sealed class GetBotPositionQueryHandler(IBotRepository bots, IBotCheckpointRepository checkpoints, IBotPositionCycleRepository cycles)
    : IRequestHandler<GetBotPositionQuery, BotPositionInternal>
{
    public async Task<BotPositionInternal> Handle(GetBotPositionQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        BotCheckpoint? checkpoint = await checkpoints.Get(bot.Id, cancellationToken);
        ExternalAccountCheckpoint? account = checkpoint?.Engine.Account;
        IReadOnlyList<BotPositionCycle> recent = await cycles.List(bot.Id, query.CycleLimit, cancellationToken);
        decimal baseTotal = account?.BaseTotal ?? 0m;

        return new BotPositionInternal(
            baseTotal,
            account?.QuoteTotal ?? bot.AllocationCap,
            account?.CostBasis ?? 0m,
            baseTotal > 0m ? account!.CostBasis / baseTotal : null,
            account?.RealizedPnl ?? 0m,
            checkpoint?.Engine.PositionOpenedAt,
            [.. recent.Select(cycle => new BotPositionCycleInternal(cycle.Id.Value, cycle.OpenedAt, cycle.ClosedAt, cycle.RealizedPnl))]);
    }
}