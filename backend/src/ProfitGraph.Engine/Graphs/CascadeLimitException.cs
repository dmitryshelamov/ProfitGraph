using ProfitGraph.Engine.Common;

namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     Structured same-price cascade failure retaining the generating node and causal occurrence.
/// </summary>
public sealed class CascadeLimitException(string nodeId, long? causationId, int count) : EngineException(
    $"Same-price cascade limit exceeded at node '{nodeId}', cause {causationId}, count {count}.")
{
    public string NodeId { get; } = nodeId;
    public long? CausationId { get; } = causationId;
    public int Count { get; } = count;
}