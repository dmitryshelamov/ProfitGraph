namespace ProfitGraph.Domain.Bots.Leases;

/// <summary>
///     Identifies a runtime process that holds bot leases; never empty.
/// </summary>
public readonly record struct LeaseHolderId
{
    public Guid Value { get; }

    public LeaseHolderId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static LeaseHolderId New()
    {
        return new LeaseHolderId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}