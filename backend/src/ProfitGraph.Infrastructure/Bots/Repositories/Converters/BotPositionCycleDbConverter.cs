using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class BotPositionCycleDbConverter
{
    public static BotPositionCycle ToDomain(this BotPositionCycleDb row)
    {
        return BotPositionCycle.Restore(new BotPositionCycleId(row.Id), new BotId(row.BotId), row.OpenedAt, row.RealizedPnlAtOpen, row.ClosedAt, row.RealizedPnl);
    }

    public static BotPositionCycleDb ToDb(this BotPositionCycle cycle)
    {
        return new BotPositionCycleDb
        {
            Id = cycle.Id.Value,
            BotId = cycle.BotId.Value,
            OpenedAt = cycle.OpenedAt,
            RealizedPnlAtOpen = cycle.RealizedPnlAtOpen,
            ClosedAt = cycle.ClosedAt,
            RealizedPnl = cycle.RealizedPnl
        };
    }
}