using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotAccount;

/// <summary>
///     The summary of the exchange account the owner's bot trades on (ADR 0049), from stored data only: another user's bot
///     answers <c>bot_not_found</c>.
/// </summary>
public sealed record GetBotAccountQuery(Guid OwnerId, Guid BotId) : IQuery<BotAccountInternal>;