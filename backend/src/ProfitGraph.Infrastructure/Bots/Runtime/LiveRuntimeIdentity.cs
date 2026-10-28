using ProfitGraph.Domain.Bots.Leases;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     The lease holder of this live process, shared by its bot runners and its intent dispatcher: a submit is delivered
///     only by the process that runs the bot.
/// </summary>
internal sealed class LiveRuntimeIdentity
{
    public LeaseHolderId HolderId { get; } = LeaseHolderId.New();
}