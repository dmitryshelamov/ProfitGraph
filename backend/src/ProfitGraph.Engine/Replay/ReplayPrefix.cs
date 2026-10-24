using System.Collections;

using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Read-only prefix of immutable prepared candles; the backing history is never exposed.
/// </summary>
internal sealed class ReplayPrefix : IReadOnlyList<Candle>
{
    private readonly IReadOnlyList<Candle> _candles;

    public int Count { get; }

    public ReplayPrefix(IReadOnlyList<Candle> candles, int count)
    {
        _candles = candles is ReplayPrefix prefix ? prefix._candles : candles;
        Count = count;
    }

    public Candle this[int index] => index >= 0 && index < Count ? _candles[index] : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<Candle> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return _candles[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}