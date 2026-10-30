namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     Identifies an auto trade rule; never empty.
/// </summary>
public readonly record struct ScanAutoTradeRuleId
{
    public Guid Value { get; }

    public ScanAutoTradeRuleId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An auto trade rule identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static ScanAutoTradeRuleId New()
    {
        return new ScanAutoTradeRuleId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}