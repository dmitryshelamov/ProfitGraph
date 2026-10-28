namespace ProfitGraph.Domain.Bots.Alerts;

/// <summary>
///     Identifies an alert of a bot; never empty.
/// </summary>
public readonly record struct BotAlertId
{
    public Guid Value { get; }

    public BotAlertId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static BotAlertId New()
    {
        return new BotAlertId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}