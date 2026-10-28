namespace ProfitGraph.Domain.Accounts.Credentials;

/// <summary>
///     The final four characters of an API key, kept in plain text so the owner can tell keys apart.
/// </summary>
public sealed record ApiKeyLast4
{
    public const int Length = 4;

    public string Value { get; }

    public ApiKeyLast4(string? value)
    {
        if (value is not { Length: Length } || value.Any(char.IsWhiteSpace))
        {
            throw new AccountRuleException(AccountRuleException.InvalidApiKey, $"The visible part of an API key has exactly {Length} characters.");
        }

        Value = value;
    }

    /// <summary>
    ///     Takes the final characters of a whole key; a key shorter than <see cref="Length" /> is invalid.
    /// </summary>
    public static ApiKeyLast4 Of(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);

        return new ApiKeyLast4(apiKey.Length < Length ? null : apiKey[^Length..]);
    }

    public override string ToString()
    {
        return Value;
    }
}