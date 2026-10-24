using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.AcquireBotLease;

/// <summary>
///     Takes the bot's lease for a runtime when the bot may be leased and no other runtime holds it; returns the epoch, or
///     null when the lease was not taken.
/// </summary>
public sealed record AcquireBotLeaseCommand(Guid BotId, Guid HolderId, TimeSpan Duration) : ICommand<long?>;