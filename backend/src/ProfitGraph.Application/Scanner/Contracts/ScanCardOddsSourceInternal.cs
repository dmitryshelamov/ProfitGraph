using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A preset backtest a pair's cards take their odds from (ADR 0071, section 1; ADR 0073): a completed one of the owner on the venue
///     with cards, its preset, period, the odds by kind and band, and the weight its counts carry among the sources.
/// </summary>
public sealed record ScanCardOddsSourceInternal(
    Guid BacktestId,
    string PresetName,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ScanCardOdds> Odds,
    decimal Weight = 1m);