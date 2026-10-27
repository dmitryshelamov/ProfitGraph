using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The outcome of the proof that an order T-Invest no longer knows left nothing behind (ADR 0030): the checks that held,
///     in order, and the first one that did not, if any. It is proven only when none failed.
/// </summary>
internal sealed record TInvestAbsenceProof(IReadOnlyList<string> Passed, string? Failed)
{
    public bool IsProven => Failed is null;

    public OrderAbsenceProofInternal ToInternal()
    {
        return new OrderAbsenceProofInternal(Passed);
    }
}
