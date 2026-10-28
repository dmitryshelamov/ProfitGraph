namespace ProfitGraph.Domain.Bots.Positions;

/// <summary>
///     Identifies a position cycle of a bot; never empty.
/// </summary>
public readonly record struct BotPositionCycleId
{
    public Guid Value { get; }

    public BotPositionCycleId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static BotPositionCycleId New()
    {
        return new BotPositionCycleId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}