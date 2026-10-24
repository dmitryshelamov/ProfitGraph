namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     Identifies a user account; never empty.
/// </summary>
public readonly record struct UserId
{
    public Guid Value { get; }

    public UserId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A user identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static UserId New()
    {
        return new UserId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}