namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Run-local decimal EMA: seeded with the simple mean of the first period samples, then
///     EMA = (2·x + (period − 1)·EMA') / (period + 1), which is alpha = 2 / (period + 1) with a single rounding per sample.
/// </summary>
/// <remarks>An unavailable sample discards the seed and the average, so the next value needs period fresh samples.</remarks>
internal sealed class ExponentialMovingAverage
{
    private readonly int _period;
    private int _count;
    private decimal _sum;
    private decimal? _value;

    public ExponentialMovingAverage(int period)
    {
        _period = period;
    }

    public decimal? Add(decimal? sample)
    {
        if (!sample.HasValue)
        {
            _count = 0;
            _sum = 0m;
            _value = null;

            return null;
        }

        if (_value.HasValue)
        {
            _value = (2m * sample.Value + (_period - 1) * _value.Value) / (_period + 1);

            return _value;
        }

        _sum += sample.Value;
        _count++;

        if (_count == _period)
        {
            _value = _sum / _period;
        }

        return _value;
    }
}