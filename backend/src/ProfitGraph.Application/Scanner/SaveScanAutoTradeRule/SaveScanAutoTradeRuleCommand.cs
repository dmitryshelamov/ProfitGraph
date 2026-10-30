using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.SaveScanAutoTradeRule;

/// <summary>
///     Creates or changes the auto trade rule of the user's own preset (ADR 0071, section 4); a changed rule is off. Omitted limits take
///     their defaults: three open trades, ten a day and a daily loss of three times the venue's trade settings' risk.
/// </summary>
public sealed record SaveScanAutoTradeRuleCommand(
    Guid UserId,
    Guid PresetId,
    PositionDirection Direction,
    ScanScenario Scenario,
    string Timeframe,
    decimal MinExpectedR,
    decimal? MinChance,
    int? MaxOpenTrades,
    int? MaxTradesPerDay,
    decimal? DailyLossLimit) : ICommand<ScanAutoTradeRuleInternal>;