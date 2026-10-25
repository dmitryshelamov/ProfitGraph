using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.ResolveManualOrder;

/// <summary>
///     The lease holder rejects a manual order without the engine, for example one that expired before its root; false when
///     the lease was lost.
/// </summary>
public sealed record ResolveManualOrderCommand(Guid BotId, Guid HolderId, long Epoch, Guid RequestId, string Reason) : ICommand<bool>;