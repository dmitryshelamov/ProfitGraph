using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     A feature's last word on a bot's submit right before it is sent (ADR 0067): the dispatcher asks every guard after the bot's own
///     risk limits and rejects the submit with <c>RiskLimit</c> when one refuses. A guard answers for the submits it owns and passes the
///     others.
/// </summary>
public interface IOrderSubmitGuard
{
    /// <summary>
    ///     Why the submit must not go now, or null.
    /// </summary>
    Task<string?> Refusal(BotId botId, OrderSnapshot order, CancellationToken cancellationToken);
}