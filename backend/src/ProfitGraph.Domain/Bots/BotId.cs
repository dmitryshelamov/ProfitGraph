namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Identifies a live bot; never empty.
/// </summary>
public readonly record struct BotId
{
    public Guid Value { get; }

    public BotId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static BotId New()
    {
        return new BotId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}