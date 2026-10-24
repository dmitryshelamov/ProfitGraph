using System.Collections;

using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Logical immutable history joining prepared warmup and replay without copying candles.
/// </summary>
internal sealed class ReplayHistory : IReadOnlyList<Candle>
{
    private readonly IReadOnlyList<Candle> _warmup;
    private readonly IReadOnlyList<Candle> _replay;

    public int Count { get; }

    public ReplayHistory(IReadOnlyList<Candle> warmup, IReadOnlyList<Candle> replay)
    {
        _warmup = warmup;
        _replay = replay;
        Count = checked(warmup.Count + replay.Count);
    }

    public Candle this[int index] => index < 0 || index >= Count
        ? throw new ArgumentOutOfRangeException(nameof(index))
        : index < _warmup.Count
            ? _warmup[index]
            : _replay[index - _warmup.Count];

    public IEnumerator<Candle> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}