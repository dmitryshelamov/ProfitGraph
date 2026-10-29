namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     Identifies a scanner backtest; never empty.
/// </summary>
public readonly record struct ScanBacktestId
{
    public Guid Value { get; }

    public ScanBacktestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A scanner backtest identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static ScanBacktestId New()
    {
        return new ScanBacktestId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}