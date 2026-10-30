using System.Collections.Immutable;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Grids;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Between-roots engine state; indicator samples are reconstructed from closed candles. Grids carry their levels (ADR 0058), and
///     an owner's position close its progress (ADR 0064).
/// </summary>
public sealed record GraphEngineCheckpoint(
    ExternalAccountCheckpoint Account,
    long NextEventId,
    long NextAttemptId,
    ImmutableDictionary<long, long> PendingSubmissionAttempts,
    ImmutableArray<PendingCancellationCheckpoint> PendingCancellations,
    Candle LastClosedCandle,
    int LastClosedCandleIndex,
    DateTimeOffset LastCandleCloseTime,
    bool Started,
    string GraphVersionId,
    ImmutableDictionary<long, string> OrderGraphVersions,
    DateTimeOffset? PositionOpenedAt = null,
    ImmutableArray<GridCheckpoint> Grids = default,
    PositionCloseState? PositionClose = null)
{
    public ImmutableArray<GridCheckpoint> Grids { get; init; } = Grids.IsDefault ? [] : Grids;
}