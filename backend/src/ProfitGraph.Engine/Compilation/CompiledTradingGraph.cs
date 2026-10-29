using System.Collections.Immutable;
using System.Collections.ObjectModel;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Compilation;

/// <summary>
///     An immutable strict trading plan.
/// </summary>
public sealed class CompiledTradingGraph
{
    private readonly IReadOnlyDictionary<PortReference, int> _slots;

    internal int SlotCount { get; }
    internal ImmutableArray<int> CachedOutputs { get; }
    public IReadOnlyList<CompiledTradingNode> Nodes { get; }
    public int WarmupCandles { get; }
    public IReadOnlyList<GraphConnection> DeferredLifecycleRoutes { get; }

    /// <summary>
    ///     A plan that covers with its buys is short (ADR 0061); every other plan is long.
    /// </summary>
    public PositionDirection Direction { get; }

    internal IReadOnlyList<CompiledTradingNode> StaticNodes { get; }
    internal IReadOnlyList<CompiledTradingNode> CandleNodes { get; }
    internal IReadOnlyList<CompiledTradingNode> DispatchNodes { get; }
    internal IReadOnlyList<CompiledTradingNode> IndicatorNodes { get; }
    internal IReadOnlyList<CompiledTradingNode> ActionNodes { get; }
    internal IReadOnlyDictionary<string, CompiledTradingNode> NodesById { get; }
    internal IReadOnlyDictionary<string, CompiledTradingNode> ActionNodesById { get; }
    internal IReadOnlyDictionary<EventKind, IReadOnlyList<CompiledTradingNode>> EventListeners { get; }

    internal CompiledTradingGraph(IEnumerable<CompiledTradingNode> nodes, IEnumerable<GraphConnection> deferredLifecycleRoutes, int warmupCandles)
    {
        Nodes = Array.AsReadOnly(nodes.ToArray());
        _slots = new ReadOnlyDictionary<PortReference, int>(
            Nodes.SelectMany(node => node.OutputBindings.Select((pair, index) =>
                    new KeyValuePair<PortReference, int>(pair.Key, node.OutputSlots[index].Key)))
                .ToDictionary(pair => pair.Key, pair => pair.Value));
        SlotCount = _slots.Count;
        CachedOutputs = Nodes.Where(node => node.Clock is NodeClock.Static or NodeClock.CandleClocked)
            .SelectMany(node => node.OutputSlots.Select(pair => pair.Key))
            .ToImmutableArray();
        StaticNodes = Array.AsReadOnly(Nodes.Where(node => node.Clock == NodeClock.Static).ToArray());
        CandleNodes = Array.AsReadOnly(Nodes.Where(node => node.Clock == NodeClock.CandleClocked).ToArray());
        DispatchNodes = Array.AsReadOnly(Nodes.Where(node => node.Clock is NodeClock.Dispatch or NodeClock.Event or NodeClock.Action).ToArray());
        IndicatorNodes = Array.AsReadOnly(Nodes.Where(node => node.Definition.IsIndicator).ToArray());
        NodesById = new ReadOnlyDictionary<string, CompiledTradingNode>(Nodes.ToDictionary(node => node.Node.Id, StringComparer.Ordinal));
        ActionNodes = Array.AsReadOnly(
            Nodes.Where(node => node.Clock == NodeClock.Action)
                .OrderBy(node => node.DependencyRank)
                .ThenBy(node => node.Node.Id, StringComparer.Ordinal)
                .ToArray());
        ActionNodesById = new ReadOnlyDictionary<string, CompiledTradingNode>(ActionNodes.ToDictionary(node => node.Node.Id, StringComparer.Ordinal));
        EventListeners = new ReadOnlyDictionary<EventKind, IReadOnlyList<CompiledTradingNode>>(
            Nodes.Where(node => node.TradingBehavior is TradingNodeBehavior.OnOrderFilled or TradingNodeBehavior.OnOrderCancelled or TradingNodeBehavior.OnOrderRejected)
                .GroupBy(node => node.TradingBehavior switch
                {
                    TradingNodeBehavior.OnOrderFilled => EventKind.Filled,
                    TradingNodeBehavior.OnOrderCancelled => EventKind.Cancelled,
                    _ => EventKind.Rejected
                })
                .ToDictionary(group => group.Key, group => (IReadOnlyList<CompiledTradingNode>)Array.AsReadOnly(group.ToArray())));
        Direction = Nodes.Any(node => node.TradingBehavior is TradingNodeBehavior.MarketCover or TradingNodeBehavior.LimitCover)
            ? PositionDirection.Short
            : PositionDirection.Long;
        WarmupCandles = warmupCandles;
        DeferredLifecycleRoutes = Array.AsReadOnly(deferredLifecycleRoutes.ToArray());
    }

    internal int SlotOf(PortReference reference)
    {
        return _slots[reference];
    }
}