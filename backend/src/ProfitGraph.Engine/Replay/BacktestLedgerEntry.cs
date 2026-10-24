using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Immutable committed mutation, with a separate ledger sequence and optional actually published event identity.
/// </summary>
public sealed record BacktestLedgerEntry(
    long Sequence,
    ReachedMarketContext Context,
    BacktestLedgerKind Kind,
    OrderSnapshot? Order = null,
    Fill? Fill = null,
    OrderRejection? Rejection = null,
    long? AttemptId = null,
    long? OccurrenceId = null,
    long? CausationId = null);