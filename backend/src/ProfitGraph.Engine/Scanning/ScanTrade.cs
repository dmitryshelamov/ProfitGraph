namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     One simulated trade after a signal: its levels, when it entered and left, why, and its result in R (the entry-to-stop distance)
///     and in percent, both after the assumed fee on each side.
/// </summary>
public sealed record ScanTrade(
    ScanScenario Scenario,
    decimal Entry,
    decimal Stop,
    decimal Target,
    DateTimeOffset EnteredAt,
    DateTimeOffset ExitedAt,
    decimal Exit,
    ScanTradeExit Reason,
    decimal ResultR,
    decimal ResultPercent);
