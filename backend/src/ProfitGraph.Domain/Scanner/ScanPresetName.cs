namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     A scanner preset name: trimmed, 1 to <see cref="MaxLength" /> characters.
/// </summary>
public sealed record ScanPresetName
{
    public const int MaxLength = 100;

    public string Value { get; }

    public ScanPresetName(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new ScanPresetRuleException(ScanPresetRuleException.InvalidName, $"A scanner preset name must contain 1 to {MaxLength} characters.");
        }

        Value = trimmed;
    }

    public override string ToString()
    {
        return Value;
    }
}