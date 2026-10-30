using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotPositionClose;

/// <summary>
///     The bot's newest position close (ADR 0064), whatever its status.
/// </summary>
public sealed record GetBotPositionCloseQuery(Guid OwnerId, Guid BotId) : IQuery<BotPositionCloseInternal>;