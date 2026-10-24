using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.RenewBotLease;

/// <summary>
///     Extends the holder's lease; false means the runtime lost it and must stop executing the bot.
/// </summary>
public sealed record RenewBotLeaseCommand(Guid BotId, Guid HolderId, long Epoch, TimeSpan Duration) : ICommand<bool>;