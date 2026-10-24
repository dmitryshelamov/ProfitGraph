using FacioQuo.Stock.Indicators;

using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Run-local library boundary with bounded result retention and explicit missing-sample tracking.
/// </summary>
internal sealed class LibraryIndicatorBuffer
{
    private readonly SmaList _sma;
    private readonly bool[] _unavailable;
    private int _sampleCount;
    private int _nextSample;
    private int _unavailableCount;

    internal int RetainedResultCount => _sma.Count;

    public LibraryIndicatorBuffer(int period)
    {
        _sma = new SmaList(period) { MaxListSize = 1 };
        _unavailable = new bool[period];
    }

    public decimal? Add(DateTime timestamp, decimal? sample)
    {
        return Add(timestamp, sample.HasValue ? (double)sample.Value : double.NaN);
    }

    public decimal? AddTrueRange(DateTime timestamp, Candle candle, Candle previous)
    {
        double range = Tr.Increment((double)candle.High, (double)candle.Low, (double)previous.Close);

        return Add(timestamp, range);
    }

    private decimal? Add(DateTime timestamp, double sample)
    {
        if (_unavailable[_nextSample])
        {
            _unavailableCount--;
        }

        bool unavailable = !double.IsFinite(sample);
        _unavailable[_nextSample] = unavailable;

        if (unavailable)
        {
            _unavailableCount++;
        }

        _nextSample = (_nextSample + 1) % _unavailable.Length;
        _sampleCount = Math.Min(_sampleCount + 1, _unavailable.Length);
        _sma.Add(timestamp, unavailable ? double.NaN : sample);

        return _sampleCount == _unavailable.Length && _unavailableCount == 0 ? ToDecimal(_sma[^1].Sma) : null;
    }

    /// <summary>
    ///     The explicit double-to-decimal cast keeps 15 significant digits; non-finite and out-of-range results remain unavailable.
    /// </summary>
    private static decimal? ToDecimal(double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value))
        {
            return null;
        }

        try
        {
            return (decimal)value.Value;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}