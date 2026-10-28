namespace ProfitGraph.Domain.Accounts.Credentials;

/// <summary>
///     The owner's name for a credential: trimmed, 1 to <see cref="MaxLength" /> characters.
/// </summary>
public sealed record ExchangeCredentialLabel
{
    public const int MaxLength = 100;

    public string Value { get; }

    public ExchangeCredentialLabel(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new AccountRuleException(AccountRuleException.InvalidCredentialLabel, $"A credential label must contain 1 to {MaxLength} characters.");
        }

        Value = trimmed;
    }

    public override string ToString()
    {
        return Value;
    }
}