using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.GetBotAlerts;

/// <summary>
///     The newest alerts first: of the user's bots, of every bot for the Owner with <see cref="EveryUser" />, or of one
///     owned bot.
/// </summary>
public sealed record GetBotAlertsQuery(Guid UserId, bool EveryUser, Guid? BotId, bool UnacknowledgedOnly, int Limit) : IQuery<IReadOnlyList<BotAlertInternal>>;