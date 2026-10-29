namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     Identifies a scanner preset; never empty.
/// </summary>
public readonly record struct ScanPresetId
{
    public Guid Value { get; }

    public ScanPresetId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A scanner preset identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static ScanPresetId New()
    {
        return new ScanPresetId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}