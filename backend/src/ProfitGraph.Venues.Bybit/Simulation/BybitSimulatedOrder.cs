using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Bybit.Simulation;

/// <summary>
///     An order the simulated venue holds: whose it is and the last update it reported for it.
/// </summary>
internal sealed record BybitSimulatedOrder(BotId BotId, ExternalOrderUpdate Last)
{
    public bool IsFinal => Last.Kind is ExternalOrderUpdateKind.FinalFill or ExternalOrderUpdateKind.CancelConfirmed;
}