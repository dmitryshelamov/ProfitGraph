namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Identifies an owner's request to cancel one of a bot's orders; never empty.
/// </summary>
public readonly record struct OrderCancelRequestId
{
    public Guid Value { get; }

    public OrderCancelRequestId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static OrderCancelRequestId New()
    {
        return new OrderCancelRequestId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}