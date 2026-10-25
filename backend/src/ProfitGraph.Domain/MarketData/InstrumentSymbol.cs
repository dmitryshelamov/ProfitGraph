namespace ProfitGraph.Domain.MarketData;

/// <summary>
///     A venue's instrument symbol in its canonical form. Only venue-neutral limits apply here; the venue module turns
///     user input into this form. Single inner spaces are allowed because some venues name instruments in words
///     (ADR 0022); no other whitespace and no control character is.
/// </summary>
public sealed record InstrumentSymbol
{
    public const int MaxLength = 256;

    public string Value { get; }

    public InstrumentSymbol(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"An instrument symbol must contain 1 to {MaxLength} characters, without control characters, leading, trailing or repeated spaces.",
                nameof(value));
        }

        Value = value;
    }

    public override string ToString()
    {
        return Value;
    }

    private static bool IsValid(string? value)
    {
        if (value is not { Length: > 0 and <= MaxLength } || value[0] == ' ' || value[^1] == ' ' || value.Contains("  ", StringComparison.Ordinal))
        {
            return false;
        }

        return !value.Any(character => char.IsControl(character) || (char.IsWhiteSpace(character) && character != ' '));
    }
}