using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Scanner.Trades;

/// <summary>
///     A scanner trade or its settings the rules forbid, such as a stop above the entry or a risk that is not positive.
/// </summary>
public sealed class ScanTradeRuleException(string code, string message) : DomainException(code, message)
{
    public const string InvalidLevels = "invalid_scan_trade_levels";
    public const string InvalidSettings = "invalid_scan_trade_settings";
}