using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     An auto trade rule the rules forbid (ADR 0071): limits that are not positive, a switch out of the order <c>Off → Paper → Live</c>
///     or a switch its backtests or paper trades do not prove.
/// </summary>
public sealed class ScanAutoTradeRuleException(string code, string message) : DomainException(code, message)
{
    public const string InvalidRule = "invalid_scan_auto_trade_rule";
    public const string WrongOrder = "scan_auto_trade_wrong_order";
    public const string NotProven = "scan_auto_trade_not_proven";
}