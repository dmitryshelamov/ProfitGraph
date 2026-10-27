namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     The outcome of reading an account's identity: what the venue told, or a stable reason code with a safe detail (status
///     and venue codes only, never keys, signatures or response bodies).
/// </summary>
public sealed record AccountIdentityReadInternal(AccountIdentityInternal? Identity, string? FailureReason, string? FailureDetail)
{
    public static AccountIdentityReadInternal Observed(AccountIdentityInternal identity)
    {
        return new AccountIdentityReadInternal(identity, FailureReason: null, FailureDetail: null);
    }

    public static AccountIdentityReadInternal Failed(string reason, string? detail = null)
    {
        return new AccountIdentityReadInternal(Identity: null, reason, detail);
    }
}