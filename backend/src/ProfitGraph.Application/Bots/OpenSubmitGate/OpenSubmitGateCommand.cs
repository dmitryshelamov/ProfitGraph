using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.OpenSubmitGate;

/// <summary>
///     Opens the submit gate of the holder's lease once the bot's venue state is known; false when the lease was lost.
/// </summary>
public sealed record OpenSubmitGateCommand(Guid BotId, Guid HolderId, long Epoch) : ICommand<bool>;