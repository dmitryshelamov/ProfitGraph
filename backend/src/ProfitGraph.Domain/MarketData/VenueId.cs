namespace ProfitGraph.Domain.MarketData;

/// <summary>
///     Identifies a trading venue module: lowercase ASCII letters, digits and hyphens, 1 to <see cref="MaxLength" />
///     characters. There is no default venue.
/// </summary>
public readonly record struct VenueId
{
    private const int MaxLength = 32;

    public string Value { get; }

    public VenueId(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException($"A venue identifier must contain 1 to {MaxLength} lowercase letters, digits or hyphens.", nameof(value));
        }

        Value = value;
    }

    public static bool IsValid(string? value)
    {
        return value is { Length: > 0 and <= MaxLength } &&
            value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');
    }

    public override string ToString()
    {
        return Value;
    }
}