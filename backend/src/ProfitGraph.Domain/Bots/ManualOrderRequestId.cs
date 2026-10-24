namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Identifies a manual order request; never empty.
/// </summary>
public readonly record struct ManualOrderRequestId
{
    public Guid Value { get; }

    public ManualOrderRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static ManualOrderRequestId New()
    {
        return new ManualOrderRequestId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}