namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     Identifies a stored exchange credential; never empty.
/// </summary>
public readonly record struct ExchangeCredentialId
{
    public Guid Value { get; }

    public ExchangeCredentialId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An exchange credential identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static ExchangeCredentialId New()
    {
        return new ExchangeCredentialId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}