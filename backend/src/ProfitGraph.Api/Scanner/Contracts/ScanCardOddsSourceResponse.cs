namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A preset backtest a pair's cards take their odds from (ADR 0071, section 1; ADR 0073): its id, preset, period, completion and the
///     weight its counts carry among the sources, an exact string.
/// </summary>
public sealed record ScanCardOddsSourceResponse(Guid BacktestId, string PresetName, DateTimeOffset From, DateTimeOffset To, DateTimeOffset CompletedAt, string Weight);