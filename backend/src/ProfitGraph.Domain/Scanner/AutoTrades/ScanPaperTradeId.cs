namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     Identifies a paper trade; never empty.
/// </summary>
public readonly record struct ScanPaperTradeId
{
    public Guid Value { get; }

    public ScanPaperTradeId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A paper trade identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static ScanPaperTradeId New()
    {
        return new ScanPaperTradeId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}