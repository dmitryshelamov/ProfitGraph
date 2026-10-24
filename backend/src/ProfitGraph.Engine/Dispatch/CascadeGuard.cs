using ProfitGraph.Engine.Graphs;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Counts generated actions and events per logical market point; relabeling a phase or queue never resets it.
/// </summary>
public sealed class CascadeGuard
{
    public const int Limit = 10000;
    private int _candle = -1;
    private bool _closed;
    private decimal _price;
    private int _count;

    public void BeginOpen(int candleIndex, decimal price)
    {
        if (candleIndex <= _candle)
        {
            throw new ArgumentException("Each replay Open must start a new increasing candle root.", nameof(candleIndex));
        }

        _candle = candleIndex;
        _closed = false;
        Reset(price);
    }

    public void BeginClose(int candleIndex, decimal price)
    {
        if (candleIndex != _candle || _closed)
        {
            throw new ArgumentException("Close must start once after its candle Open root.", nameof(candleIndex));
        }

        _closed = true;
        Reset(price);
    }

    public void AdvancePrice(decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);

        if (_candle < 0)
        {
            throw new InvalidOperationException("A replay root must be initialized before price advance.");
        }

        if (price != _price)
        {
            Reset(price);
        }
    }

    public void BeginExternal(int candleIndex, decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candleIndex);

        if (candleIndex < _candle)
        {
            throw new ArgumentException("External updates cannot move behind the last closed candle.", nameof(candleIndex));
        }

        _candle = candleIndex;
        _closed = true;
        Reset(price);
    }

    public void Consume(string nodeId, long? causationId)
    {
        if (_candle < 0)
        {
            throw new InvalidOperationException("A replay root must be initialized before causal dispatch.");
        }

        _count++;

        if (_count > Limit)
        {
            throw new CascadeLimitException(nodeId, causationId, _count);
        }
    }

    private void Reset(decimal price)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        _price = price;
        _count = 0;
    }
}