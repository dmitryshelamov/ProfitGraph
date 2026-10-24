using System.Collections.Concurrent;

namespace ProfitGraph.Api.Backtesting.Realtime;

/// <summary>
///     Runs that someone follows, with the last observation this API instance sent for each; a run is forgotten when
///     it finishes.
/// </summary>
public sealed class BacktestProgressSubscriptions
{
    private readonly ConcurrentDictionary<Guid, long> _sent = new ConcurrentDictionary<Guid, long>();

    public static string Group(Guid runId)
    {
        return $"backtest:{runId:D}";
    }

    /// <summary>
    ///     Starts following a run after <paramref name="lastSequence" />; a run already followed keeps its position.
    /// </summary>
    public void Watch(Guid runId, long lastSequence)
    {
        _sent.TryAdd(runId, lastSequence);
    }

    public bool TryGetSent(Guid runId, out long lastSequence)
    {
        return _sent.TryGetValue(runId, out lastSequence);
    }

    public void MarkSent(Guid runId, long lastSequence)
    {
        _sent[runId] = lastSequence;
    }

    public void Forget(Guid runId)
    {
        _sent.TryRemove(runId, out _);
    }
}