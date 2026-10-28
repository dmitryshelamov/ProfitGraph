using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.Positions;

/// <summary>
///     A bot's position from the fill that opened it to the fill that made it flat again (ADR 0021). Its realized profit
///     is the engine's realized profit over that span.
/// </summary>
public sealed class BotPositionCycle : AggregateRoot
{
    public BotPositionCycleId Id { get; }
    public BotId BotId { get; }
    public DateTimeOffset OpenedAt { get; }

    /// <summary>
    ///     The engine's realized profit when the position opened.
    /// </summary>
    public decimal RealizedPnlAtOpen { get; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    ///     The profit realized in this cycle, once it closed.
    /// </summary>
    public decimal? RealizedPnl { get; private set; }

    private BotPositionCycle(BotPositionCycleId id, BotId botId, DateTimeOffset openedAt, decimal realizedPnlAtOpen, DateTimeOffset? closedAt, decimal? realizedPnl)
    {
        Id = id;
        BotId = botId;
        OpenedAt = openedAt;
        RealizedPnlAtOpen = realizedPnlAtOpen;
        ClosedAt = closedAt;
        RealizedPnl = realizedPnl;
    }

    public static BotPositionCycle Open(BotPositionCycleId id, BotId botId, DateTimeOffset openedAt, decimal realizedPnl)
    {
        var cycle = new BotPositionCycle(id, botId, openedAt, realizedPnl, closedAt: null, realizedPnl: null);
        cycle.AddDomainEvent(new BotPositionOpened(botId, id, openedAt));

        return cycle;
    }

    public static BotPositionCycle Restore(
        BotPositionCycleId id,
        BotId botId,
        DateTimeOffset openedAt,
        decimal realizedPnlAtOpen,
        DateTimeOffset? closedAt,
        decimal? realizedPnl)
    {
        return new BotPositionCycle(id, botId, openedAt, realizedPnlAtOpen, closedAt, realizedPnl);
    }

    /// <summary>
    ///     Closes the cycle with the engine's realized profit now; returns whether it was open.
    /// </summary>
    public bool Close(DateTimeOffset closedAt, decimal realizedPnl)
    {
        if (ClosedAt is not null)
        {
            return false;
        }

        ClosedAt = closedAt;
        RealizedPnl = realizedPnl - RealizedPnlAtOpen;
        AddDomainEvent(new BotPositionClosed(BotId, Id, closedAt, RealizedPnl.Value));

        return true;
    }
}