namespace ProfitGraph.Domain.MarketData;

/// <summary>
///     A venue's instrument symbol in its canonical form. Only venue-neutral limits apply here; the venue module turns
///     user input into this form.
/// </summary>
public sealed record InstrumentSymbol
{
    public const int MaxLength = 64;

    public string Value { get; }

    public InstrumentSymbol(string value)
    {
        if (value is not { Length: > 0 and <= MaxLength } || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new ArgumentException($"An instrument symbol must contain 1 to {MaxLength} characters without spaces.", nameof(value));
        }

        Value = value;
    }

    public override string ToString()
    {
        return Value;
    }
}