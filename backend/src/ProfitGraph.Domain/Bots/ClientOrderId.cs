namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The deterministic identifier a venue knows an order by. Its format belongs to the venue module; here it is an
///     opaque value of visible ASCII characters, 1 to <see cref="MaxLength" /> long.
/// </summary>
public sealed record ClientOrderId
{
    public const int MaxLength = 64;

    public string Value { get; }

    public ClientOrderId(string? value)
    {
        if (value is not { Length: > 0 and <= MaxLength } || value.Any(character => character is <= ' ' or > '~'))
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, $"A client order identifier has 1 to {MaxLength} visible ASCII characters.");
        }

        Value = value;
    }

    public override string ToString()
    {
        return Value;
    }
}