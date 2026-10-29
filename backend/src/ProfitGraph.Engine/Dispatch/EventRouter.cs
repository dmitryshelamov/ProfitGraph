using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Routes one identity to matching global listeners and only its originating action lifecycle port.
/// </summary>
public sealed class EventRouter
{
    internal void RouteInto(CompiledTradingGraph graph, EventOccurrence occurrence, EngineValue[] values, List<int> touchedSlots)
    {
        foreach (int slot in touchedSlots)
        {
            values[slot] = EngineValue.Unavailable(values[slot].ValueType);
        }

        touchedSlots.Clear();
        string? owner = occurrence.Payload switch
        {
            FillPayload payload => payload.OriginNodeId,
            OrderPayload payload => payload.Order.OriginNodeId,
            RejectionPayload payload => payload.Rejection.OriginNodeId,
            CancelResultPayload payload => payload.OriginNodeId,
            GridPayload payload => payload.OriginNodeId,
            _ => null
        };
        string? tag = occurrence.Payload switch
        {
            FillPayload payload => payload.Fill.Tag, OrderPayload payload => payload.Order.Tag, RejectionPayload payload => payload.Rejection.Tag, _ => null
        };
        string? side = occurrence.Payload is FillPayload fill ? fill.Fill.Side.ToString() : null;

        foreach (CompiledTradingNode node in graph.EventListeners.GetValueOrDefault(occurrence.Kind) ?? Array.Empty<CompiledTradingNode>())
        {
            string Setting(string key, string fallback) =>
                node.Node.Parameters.GetValueOrDefault(key) is FixedParamValue { Value: TextParamLiteral text } ? text.Value : fallback;

            if ((Setting("tag", "") == "" || Setting("tag", "") == tag) &&
                (node.TradingBehavior != TradingNodeBehavior.OnOrderFilled || Setting("side", "Any") == "Any" || Setting("side", "Any") == side))
            {
                int slot = node.OutputSlotsByKey["out"];
                values[slot] = EngineValue.FromEvent(occurrence);
                touchedSlots.Add(slot);
            }
        }

        if (owner is not null && graph.ActionNodesById.TryGetValue(owner, out CompiledTradingNode? action))
        {
            string? port = occurrence.Kind switch
            {
                EventKind.Submitted => "submitted", EventKind.Filled => "filled", EventKind.Cancelled => "cancelled", EventKind.Rejected => "rejected",
                EventKind.CancelCompleted => "completed", EventKind.GridStarted => "started", EventKind.GridStopped => "stopped", _ => null
            };

            if (port is not null && action.OutputTypes.ContainsKey(port))
            {
                int slot = action.OutputSlotsByKey[port];
                values[slot] = EngineValue.FromEvent(occurrence);
                touchedSlots.Add(slot);
            }
        }
    }
}