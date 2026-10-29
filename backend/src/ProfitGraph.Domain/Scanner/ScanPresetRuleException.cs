using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     A scanner preset the preset rules forbid, such as an empty name or a condition without bounds.
/// </summary>
public sealed class ScanPresetRuleException(string code, string message) : DomainException(code, message)
{
    public const string InvalidName = "invalid_scan_preset_name";
    public const string InvalidCondition = "invalid_scan_preset_condition";
}