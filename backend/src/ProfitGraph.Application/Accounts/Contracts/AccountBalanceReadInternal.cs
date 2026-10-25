namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     The outcome of reading an account's balance: an observation, or a stable reason code with a safe detail (status
///     and venue codes only, never keys, signatures or response bodies).
/// </summary>
public sealed record AccountBalanceReadInternal(AccountBalanceObservationInternal? Observation, string? FailureReason, string? FailureDetail)
{
    public static AccountBalanceReadInternal Observed(AccountBalanceObservationInternal observation)
    {
        return new AccountBalanceReadInternal(observation, FailureReason: null, FailureDetail: null);
    }

    public static AccountBalanceReadInternal Failed(string reason, string? detail = null)
    {
        return new AccountBalanceReadInternal(Observation: null, reason, detail);
    }
}