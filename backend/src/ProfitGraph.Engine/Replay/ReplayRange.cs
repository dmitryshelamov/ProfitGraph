using System.Collections;

using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Read-only range over the original prepared history; the backing sequence is never exposed.
/// </summary>
internal sealed class ReplayRange : IReadOnlyList<Candle>
{
    private readonly IReadOnlyList<Candle> _history;
    private readonly int _start;

    public int Count { get; }

    public ReplayRange(IReadOnlyList<Candle> history, int start, int count)
    {
        _history = history;
        _start = start;
        Count = count;
    }

    public Candle this[int index] => index >= 0 && index < Count ? _history[_start + index] : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<Candle> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return _history[_start + index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}