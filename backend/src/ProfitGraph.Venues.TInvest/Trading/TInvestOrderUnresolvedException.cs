namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     T-Invest gave no definitive answer about an order, so its state is unknown until a lookup by request ID resolves it.
///     The intent retries; nothing is sent again. <see cref="Reason" /> names the condition that failed, a fixed code without
///     any broker text, so it is safe to log; <see cref="IsTransient" /> marks a state that may only be read ahead of its
///     own parts and is worth reading again.
/// </summary>
internal sealed class TInvestOrderUnresolvedException(string reason = "Unspecified", bool isTransient = false)
    : Exception("The T-Invest order state is not resolved yet: " + reason + ".")
{
    public string Reason { get; } = reason;

    public bool IsTransient { get; } = isTransient;
}