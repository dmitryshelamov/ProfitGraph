namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     The part of a credential its owner may see in full: a non-secret identifier such as an account number, a login or an
///     API access key, 1 to <see cref="MaxLength" /> characters without spaces.
/// </summary>
public sealed record CredentialPublicId
{
    public const int MaxLength = 128;

    public string Value { get; }

    public CredentialPublicId(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length is 0 or > MaxLength || trimmed.Any(char.IsWhiteSpace))
        {
            throw new AccountRuleException(AccountRuleException.InvalidPublicId, $"The visible part of a credential has 1 to {MaxLength} characters without spaces.");
        }

        Value = trimmed;
    }

    public override string ToString()
    {
        return Value;
    }
}