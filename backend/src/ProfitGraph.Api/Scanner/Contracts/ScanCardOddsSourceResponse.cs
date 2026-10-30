namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     The preset backtest a pair's cards take their odds from (ADR 0071, section 1): its id, preset, period and completion.
/// </summary>
public sealed record ScanCardOddsSourceResponse(Guid BacktestId, string PresetName, DateTimeOffset From, DateTimeOffset To, DateTimeOffset CompletedAt);