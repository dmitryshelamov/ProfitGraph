using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The preset backtest a pair's cards take their odds from (ADR 0071, section 1): the owner's latest completed one on the venue
///     with cards, its preset, period and the odds by kind and band.
/// </summary>
public sealed record ScanCardOddsSourceInternal(
    Guid BacktestId,
    string PresetName,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ScanCardOdds> Odds);