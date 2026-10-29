namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A simulated trade of one scenario after a signal: levels, entry and exit, the reason it left and its result in R and in percent
///     after fees, as exact decimal strings.
/// </summary>
public sealed record ScanTradeResponse(
    string Scenario,
    string Entry,
    string Stop,
    string Target,
    DateTimeOffset EnteredAt,
    DateTimeOffset ExitedAt,
    string Exit,
    string Reason,
    string ResultR,
    string ResultPercent);