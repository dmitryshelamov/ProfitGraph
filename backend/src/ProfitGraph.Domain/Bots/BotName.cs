namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The owner's name for a bot: trimmed, 1 to <see cref="MaxLength" /> characters. A bot without one is shown by its symbol
///     and timeframe.
/// </summary>
public sealed record BotName
{
    public const int MaxLength = 100;

    public string Value { get; }

    public BotName(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new BotRuleException(BotRuleException.InvalidName, $"A bot name must contain 1 to {MaxLength} characters.");
        }

        Value = trimmed;
    }

    public override string ToString()
    {
        return Value;
    }
}