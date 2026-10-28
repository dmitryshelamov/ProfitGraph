using ProfitGraph.Domain.Bots.OrderWaiting;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     A venue's answer to whether a submit may be sent now (ADR 0024): ready, or the reason it must wait, until when if
///     known, the price bounds that keep it when a price band holds it, and when to ask again.
/// </summary>
public sealed record OrderReadinessInternal(
    OrderWaitingCode? Code,
    DateTimeOffset? Until = null,
    decimal? Low = null,
    decimal? High = null,
    DateTimeOffset? RecheckAt = null)
{
    public static OrderReadinessInternal Ready { get; } = new OrderReadinessInternal(Code: null);

    public bool IsReady => Code is null;
}