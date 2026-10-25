using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.KillBot;

/// <summary>
///     The owner's kill switch for one bot: it stops at once, unsent submits are rejected and open orders cancelled.
/// </summary>
public sealed record KillBotCommand(Guid OwnerId, Guid Id) : ICommand<BotInternal>;