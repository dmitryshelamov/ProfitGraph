namespace ProfitGraph.Domain.Bots.PositionCloses;

/// <summary>
///     Identifies a position close request; never empty.
/// </summary>
public readonly record struct BotPositionCloseId
{
    public Guid Value { get; }

    public BotPositionCloseId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static BotPositionCloseId New()
    {
        return new BotPositionCloseId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}