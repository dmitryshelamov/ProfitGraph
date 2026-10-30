using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.RequestPositionClose;

/// <summary>
///     The owner closes a running bot's long position (ADR 0064): the runner cancels the bot's working orders, sells what the
///     position holds at the market and stops the bot when <see cref="StopAfter" /> is set or the close fails. It takes the
///     request within a minute or the request expires; a bot has at most one close that has not ended.
/// </summary>
public sealed record RequestPositionCloseCommand(Guid OwnerId, Guid BotId, bool StopAfter) : ICommand<BotPositionCloseInternal>;