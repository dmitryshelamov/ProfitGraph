namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     A strategy name: trimmed, 1 to <see cref="MaxLength" /> characters.
/// </summary>
public sealed record StrategyName
{
    public const int MaxLength = 200;

    public string Value { get; }

    public StrategyName(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new StrategyRuleException(StrategyRuleException.InvalidName, $"A strategy name must contain 1 to {MaxLength} characters.");
        }

        Value = trimmed;
    }

    public override string ToString()
    {
        return Value;
    }
}