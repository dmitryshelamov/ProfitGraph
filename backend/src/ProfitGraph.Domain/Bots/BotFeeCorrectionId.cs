namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Identifies a recorded fee correction of a bot's order; never empty.
/// </summary>
public readonly record struct BotFeeCorrectionId
{
    public Guid Value { get; }

    public BotFeeCorrectionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static BotFeeCorrectionId New()
    {
        return new BotFeeCorrectionId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}