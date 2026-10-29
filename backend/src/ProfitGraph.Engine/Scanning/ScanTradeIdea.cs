namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     A trade a scanner trade card proposes (ADR 0060): a limit entry, a stop and a target from one of the preset backtest's scenarios,
///     its reward-to-risk at the entry, and how long its entry may wait for a fill.
/// </summary>
public sealed record ScanTradeIdea(ScanScenario Scenario, decimal Entry, decimal Stop, decimal Target, decimal RewardRisk, TimeSpan EntryValidity);