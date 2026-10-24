using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     What one drained engine root produced: the checkpoint after it, the parameter values the engine runs with and the
///     intents for the venue. It is written by the lease holder under <see cref="Epoch" />.
/// </summary>
public sealed record BotRootCommit(
    BotId BotId,
    LeaseHolderId HolderId,
    long Epoch,
    GraphEngineCheckpoint Checkpoint,
    BotParameters AppliedParameters,
    IReadOnlyList<OrderIntent> Intents);