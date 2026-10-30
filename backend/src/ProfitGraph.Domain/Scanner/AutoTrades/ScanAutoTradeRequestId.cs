namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     Identifies an auto trade request; never empty.
/// </summary>
public readonly record struct ScanAutoTradeRequestId
{
    public Guid Value { get; }

    public ScanAutoTradeRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An auto trade request identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static ScanAutoTradeRequestId New()
    {
        return new ScanAutoTradeRequestId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}