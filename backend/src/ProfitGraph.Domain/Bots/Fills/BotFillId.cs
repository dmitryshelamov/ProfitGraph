namespace ProfitGraph.Domain.Bots.Fills;

/// <summary>
///     Identifies a recorded fill of a bot; never empty.
/// </summary>
public readonly record struct BotFillId
{
    public Guid Value { get; }

    public BotFillId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static BotFillId New()
    {
        return new BotFillId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}