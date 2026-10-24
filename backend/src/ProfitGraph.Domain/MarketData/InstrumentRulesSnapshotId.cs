namespace ProfitGraph.Domain.MarketData;

/// <summary>
///     Identifies a captured instrument rules snapshot; never empty.
/// </summary>
public readonly record struct InstrumentRulesSnapshotId
{
    public Guid Value { get; }

    public InstrumentRulesSnapshotId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An instrument rules snapshot identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}