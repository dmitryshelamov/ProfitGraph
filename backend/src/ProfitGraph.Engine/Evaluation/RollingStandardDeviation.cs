namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Run-local decimal population standard deviation of the last period samples: sqrt(Σ(x − mean)² / period).
/// </summary>
/// <remarks>
///     Two passes over the window keep a flat window at exactly zero. The root is <see cref="DecimalSquareRoot" />.
///     An unavailable sample empties the window, so the next value needs period fresh samples, as SMA does.
/// </remarks>
internal sealed class RollingStandardDeviation
{
    private readonly decimal[] _window;
    private int _count;
    private int _next;

    public RollingStandardDeviation(int period)
    {
        _window = new decimal[period];
    }

    public decimal? Add(decimal? sample)
    {
        if (!sample.HasValue)
        {
            _count = 0;
            _next = 0;

            return null;
        }

        _window[_next] = sample.Value;
        _next = (_next + 1) % _window.Length;
        _count = Math.Min(_count + 1, _window.Length);

        if (_count < _window.Length)
        {
            return null;
        }

        var sum = 0m;

        foreach (decimal value in _window)
        {
            sum += value;
        }

        decimal mean = sum / _window.Length;
        var squares = 0m;

        foreach (decimal value in _window)
        {
            decimal deviation = value - mean;
            squares += deviation * deviation;
        }

        return DecimalSquareRoot.Sqrt(squares / _window.Length);
    }
}