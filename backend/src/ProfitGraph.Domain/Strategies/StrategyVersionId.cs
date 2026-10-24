namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     Identifies one saved version of a strategy; never empty.
/// </summary>
public readonly record struct StrategyVersionId
{
    public Guid Value { get; }

    public StrategyVersionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A strategy version identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static StrategyVersionId New()
    {
        return new StrategyVersionId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}