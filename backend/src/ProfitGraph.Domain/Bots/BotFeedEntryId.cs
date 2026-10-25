namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Identifies an entry of a bot's event feed; never empty.
/// </summary>
public readonly record struct BotFeedEntryId
{
    public Guid Value { get; }

    public BotFeedEntryId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static BotFeedEntryId New()
    {
        return new BotFeedEntryId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}