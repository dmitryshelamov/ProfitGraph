namespace ProfitGraph.Domain.Accounts.Snapshots;

/// <summary>
///     Identifies an account value snapshot; never empty.
/// </summary>
public readonly record struct AccountSnapshotId
{
    public Guid Value { get; }

    public AccountSnapshotId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static AccountSnapshotId New()
    {
        return new AccountSnapshotId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}