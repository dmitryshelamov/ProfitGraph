using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Which cards the preset backtest proposes at a signal (ADR 0071, section 2), as the pair's page of the venue does: the
///     timeframes whose levels give cards, whether the venue offers the pullback's and the extreme's timeframes, and whether it allows
///     shorts (ADR 0061).
/// </summary>
public sealed record ScanCardSetup(IReadOnlyList<Timeframe> LevelTimeframes, bool Pullback, bool Extremes, bool Shorts);