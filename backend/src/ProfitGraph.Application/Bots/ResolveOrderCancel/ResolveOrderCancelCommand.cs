using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.ResolveOrderCancel;

/// <summary>
///     The lease holder rejects an owner's order cancel without the engine, for example one that expired before its root; false
///     when the lease was lost.
/// </summary>
public sealed record ResolveOrderCancelCommand(Guid BotId, Guid HolderId, long Epoch, Guid RequestId, string Reason) : ICommand<bool>;