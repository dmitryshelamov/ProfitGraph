namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     The auto trade rule of a preset (ADR 0071, section 4): which card of a signal to take and the least expected R and chance of its
///     odds, and the limits; omitted limits keep the rule's or take their defaults. Decimals are exact strings.
/// </summary>
public sealed record ScanAutoTradeRuleRequest(
    string Direction,
    string Scenario,
    string Timeframe,
    string MinExpectedR,
    string? MinChance = null,
    int? MaxOpenTrades = null,
    int? MaxTradesPerDay = null,
    string? DailyLossLimit = null);