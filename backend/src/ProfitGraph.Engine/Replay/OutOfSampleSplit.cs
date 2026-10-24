namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Доля последних баров диапазона, отложенная как out-of-sample хвост: оптимизация видит только in-sample часть.
/// </summary>
public sealed record OutOfSampleSplit
{
    public static readonly OutOfSampleSplit Default = new OutOfSampleSplit(fraction: 0.2m);
    public static readonly OutOfSampleSplit None = new OutOfSampleSplit(fraction: 0m);

    public decimal Fraction { get; }

    private OutOfSampleSplit(decimal fraction)
    {
        Fraction = fraction;
    }

    public static OutOfSampleSplit Create(decimal fraction)
    {
        if (fraction is < 0m or >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Out-of-sample fraction must be in [0, 1).");
        }

        return new OutOfSampleSplit(fraction);
    }

    /// <summary>
    ///     Делит бары по количеству: хвост = floor(barCount * Fraction), in-sample = остаток в начале диапазона.
    /// </summary>
    public SampleRanges Split(int barCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(barCount);

        var outOfSampleBars = (int)decimal.Floor(barCount * Fraction);
        int inSampleBars = barCount - outOfSampleBars;

        if (inSampleBars == 0 || (Fraction > 0m && outOfSampleBars == 0))
        {
            throw new SampleSplitException(barCount, Fraction);
        }

        return new SampleRanges(inSampleBars, outOfSampleBars);
    }
}