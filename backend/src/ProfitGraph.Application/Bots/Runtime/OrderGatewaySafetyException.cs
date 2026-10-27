using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     The venue reported a state the bot cannot trade through, such as a fee in an asset the engine cannot account; the
///     bot degrades with <see cref="Reason" /> instead of retrying. <see cref="Detail" /> is a short text for the owner that says
///     why, such as the check that stopped an automatic close (ADR 0030, ADR 0031).
/// </summary>
public class OrderGatewaySafetyException(BotStatusReason reason, string message, string? detail = null) : Exception(message)
{
    public BotStatusReason Reason { get; } = reason;

    public string? Detail { get; } = detail;
}