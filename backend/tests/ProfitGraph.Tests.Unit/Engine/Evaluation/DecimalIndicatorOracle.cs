using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Engine.Evaluation;

/// <summary>
///     The pre-library decimal formulas retained exclusively as an independent numerical reference.
/// </summary>
internal sealed class DecimalIndicatorOracle
{
    private readonly Queue<decimal?> _window = new Queue<decimal?>();
    private Candle? _previous;

    public decimal? Sma(decimal? sample, int period)
    {
        _window.Enqueue(sample);

        return Mean(period);
    }

    public decimal? Atr(Candle candle, int period)
    {
        if (_previous is not null)
        {
            _window.Enqueue(
                decimal.Max(
                    candle.High - candle.Low,
                    decimal.Max(decimal.Abs(candle.High - _previous.Close), decimal.Abs(candle.Low - _previous.Close))));
        }

        _previous = candle;

        return Mean(period);
    }

    private decimal? Mean(int period)
    {
        while (_window.Count > period)
        {
            _window.Dequeue();
        }

        return _window.Count == period && _window.All(value => value.HasValue) ? _window.Sum(value => value!.Value) / period : null;
    }
}