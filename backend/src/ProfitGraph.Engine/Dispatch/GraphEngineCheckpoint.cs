using System.Collections.Immutable;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Between-roots engine state; indicator samples are reconstructed from closed candles.
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
    DateTimeOffset? PositionOpenedAt = null);