namespace ProfitGraph.Domain.Scanner.Trades;

/// <summary>
///     Identifies a scanner trade; never empty.
/// </summary>
public readonly record struct ScanTradeId
{
    public Guid Value { get; }

    public ScanTradeId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A scanner trade identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static ScanTradeId New()
    {
        return new ScanTradeId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}