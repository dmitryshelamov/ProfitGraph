using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The backtests a card's odds are pooled from with their weights, newest first, and the odds model built from them (ADR 0073).
/// </summary>
public sealed record ScanCardOddsBasisInternal(IReadOnlyList<ScanCardOddsSourceInternal> Sources, ScanCardOddsModel Model);