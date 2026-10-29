using System.Collections.Immutable;

namespace ProfitGraph.Engine.Grids;

/// <summary>
///     A grid's state between roots (ADR 0058, section 10), so a restarted bot continues with its anchors and levels.
/// </summary>
public sealed record GridCheckpoint(
    string NodeId,
    GridPhase Phase,
    decimal? Anchor,
    decimal? FirstAnchor,
    bool SellOnStop,
    bool StopSubmitted,
    ImmutableArray<GridLevelCheckpoint> Levels);