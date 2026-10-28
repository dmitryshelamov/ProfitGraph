using ProfitGraph.Domain.Bots.OrderWaiting;

namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     Why a submit the bot accepted is held in the core instead of being sent (ADR 0024): the reason, since when, until
///     when if known, and the price bounds that keep it when a price band holds it.
/// </summary>
public sealed record OrderDeferral(OrderWaitingCode Code, DateTimeOffset Since, DateTimeOffset? Until = null, decimal? Low = null, decimal? High = null)
{
    public bool IsValid => Enum.IsDefined(Code) && Low is not <= 0m && High is not <= 0m && !(Low > High) && (Until is null || Until >= Since);
}