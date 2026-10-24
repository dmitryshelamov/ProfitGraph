namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     Identifies a strategy; never empty.
/// </summary>
public readonly record struct StrategyId
{
    public Guid Value { get; }

    public StrategyId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A strategy identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static StrategyId New()
    {
        return new StrategyId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}