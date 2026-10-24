namespace ProfitGraph.Domain.Jobs;

/// <summary>
///     Identifies a background job; never empty.
/// </summary>
public readonly record struct BackgroundJobId
{
    public Guid Value { get; }

    public BackgroundJobId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A background job identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static BackgroundJobId New()
    {
        return new BackgroundJobId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}