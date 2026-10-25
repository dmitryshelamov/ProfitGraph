using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.RejectBotStrategyVersion;

/// <summary>
///     The lease holder records that the bot's engine could not switch to a saved version; false when the lease was lost.
/// </summary>
public sealed record RejectBotStrategyVersionCommand(Guid BotId, Guid HolderId, long Epoch, Guid StrategyVersionId) : ICommand<bool>;