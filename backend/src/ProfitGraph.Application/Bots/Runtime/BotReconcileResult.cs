using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     How a reconciliation ended: the venue could not answer now, the bot degraded with a safety reason, or it may trade.
/// </summary>
public sealed record BotReconcileResult(bool VenueAnswered, BotStatusReason? Degradation)
{
    public static BotReconcileResult Unavailable { get; } = new BotReconcileResult(VenueAnswered: false, Degradation: null);
}