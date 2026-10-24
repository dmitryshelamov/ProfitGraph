using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.ReleaseBotLease;

/// <summary>
///     Gives the holder's lease up so another runtime may take the bot at once.
/// </summary>
public sealed record ReleaseBotLeaseCommand(Guid BotId, Guid HolderId, long Epoch) : ICommand;