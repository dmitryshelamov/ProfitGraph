using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     The venue reported a state the bot cannot trade through, such as a fee in an asset the engine cannot account; the
///     bot degrades with <see cref="Reason" /> instead of retrying.
/// </summary>
public class OrderGatewaySafetyException(BotStatusReason reason, string message) : Exception(message)
{
    public BotStatusReason Reason { get; } = reason;
}