using System.Collections.Concurrent;

namespace ProfitGraph.Api.Optimization.Realtime;

/// <summary>
///     Optimizations that someone follows, with the last trial this API instance sent for each; an optimization is
///     forgotten when it finishes.
/// </summary>
public sealed class OptimizationProgressSubscriptions
{
    private readonly ConcurrentDictionary<Guid, long> _sent = new ConcurrentDictionary<Guid, long>();

    public static string Group(Guid optimizationId)
    {
        return $"optimization:{optimizationId:D}";
    }

    /// <summary>
    ///     Starts following an optimization after <paramref name="lastSequence" />; one already followed keeps its position.
    /// </summary>
    public void Watch(Guid optimizationId, long lastSequence)
    {
        _sent.TryAdd(optimizationId, lastSequence);
    }

    public bool TryGetSent(Guid optimizationId, out long lastSequence)
    {
        return _sent.TryGetValue(optimizationId, out lastSequence);
    }

    public void MarkSent(Guid optimizationId, long lastSequence)
    {
        _sent[optimizationId] = lastSequence;
    }

    public void Forget(Guid optimizationId)
    {
        _sent.TryRemove(optimizationId, out _);
    }
}