namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     Identifies an order intent; never empty.
/// </summary>
public readonly record struct OrderIntentId
{
    public Guid Value { get; }

    public OrderIntentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static OrderIntentId New()
    {
        return new OrderIntentId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}