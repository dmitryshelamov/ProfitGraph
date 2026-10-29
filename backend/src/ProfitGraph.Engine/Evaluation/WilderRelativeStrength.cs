namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Run-local decimal RSI with Wilder smoothing over the changes between consecutive samples.
/// </summary>
/// <remarks>
///     The first averages of gains and losses are simple means of the first period changes (period + 1 samples); later
///     ones are A = (A' · (period − 1) + change) / period. RSI = 100 − 100 / (1 + G / L) is computed as 100 · G / (G + L):
///     no losses give 100, no gains give 0, and a window without any change gives the neutral 50.
///     An unavailable sample discards the history, so the next value needs period + 1 fresh samples.
/// </remarks>
internal sealed class WilderRelativeStrength
{
    private readonly int _period;
    private decimal? _previous;
    private int _changes;
    private decimal _gain;
    private decimal _loss;

    public WilderRelativeStrength(int period)
    {
        _period = period;
    }

    public decimal? Add(decimal? sample)
    {
        if (!sample.HasValue)
        {
            _previous = null;
            _changes = 0;
            _gain = 0m;
            _loss = 0m;

            return null;
        }

        decimal? previous = _previous;
        _previous = sample;

        if (!previous.HasValue)
        {
            return null;
        }

        decimal change = sample.Value - previous.Value;
        decimal gain = change > 0m ? change : 0m;
        decimal loss = change < 0m ? -change : 0m;

        if (_changes < _period)
        {
            _gain += gain;
            _loss += loss;
            _changes++;

            if (_changes < _period)
            {
                return null;
            }

            _gain /= _period;
            _loss /= _period;
        }
        else
        {
            _gain = (_gain * (_period - 1) + gain) / _period;
            _loss = (_loss * (_period - 1) + loss) / _period;
        }

        decimal total = _gain + _loss;

        return total == 0m ? 50m : 100m * _gain / total;
    }
}