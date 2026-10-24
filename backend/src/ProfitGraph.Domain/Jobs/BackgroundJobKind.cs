namespace ProfitGraph.Domain.Jobs;

/// <summary>
///     The stable name of a kind of work, stored with every job: lowercase letters, digits and underscores, 1 to 64 characters.
/// </summary>
public readonly record struct BackgroundJobKind
{
    private const int MaxLength = 64;

    public string Value { get; }

    public BackgroundJobKind(string value)
    {
        if (value is not { Length: > 0 and <= MaxLength } ||
            !value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_'))
        {
            throw new ArgumentException($"A job kind must contain 1 to {MaxLength} lowercase letters, digits or underscores.", nameof(value));
        }

        Value = value;
    }

    public override string ToString()
    {
        return Value;
    }
}