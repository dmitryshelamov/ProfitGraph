using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.FeeCorrections;
using ProfitGraph.Domain.Bots.Fills;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     What one drained engine root produced: the checkpoint after it, the parameter values the engine runs with and the
///     intents for the venue, with the owner's request it resolved, if any. It is written by the lease holder under
///     <see cref="Epoch" />.
/// </summary>
public sealed record BotRootCommit(
    BotId BotId,
    LeaseHolderId HolderId,
    long Epoch,
    GraphEngineCheckpoint Checkpoint,
    BotParameters AppliedParameters,
    IReadOnlyList<OrderIntent> Intents,
    BotRootManualOrder? ManualOrder = null,
    IReadOnlyList<BotFill>? Fills = null,
    BotRootPosition? Position = null,
    IReadOnlyList<BotFeeCorrection>? FeeCorrections = null,
    BotRootOrderCancel? OrderCancel = null);