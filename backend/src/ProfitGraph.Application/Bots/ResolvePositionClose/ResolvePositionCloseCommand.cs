using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.ResolvePositionClose;

/// <summary>
///     The lease holder fails a position close that expired before its root; false when the lease was lost.
/// </summary>
public sealed record ResolvePositionCloseCommand(Guid BotId, Guid HolderId, long Epoch, Guid RequestId, string Reason) : ICommand<bool>;