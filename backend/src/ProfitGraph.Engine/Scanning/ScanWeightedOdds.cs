namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The card odds of one preset backtest and the weight its counts carry when backtests are pooled (ADR 0073): newer periods weigh
///     more.
/// </summary>
public sealed record ScanWeightedOdds(IReadOnlyList<ScanCardOdds> Odds, decimal Weight);