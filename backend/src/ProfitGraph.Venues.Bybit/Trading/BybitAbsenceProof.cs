using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     The outcome of the proof that a submit Bybit does not know never took effect (ADR 0030): the checks that held, in order,
///     and the first that did not, if any.
/// </summary>
internal sealed record BybitAbsenceProof(IReadOnlyList<string> Passed, string? Failed)
{
    public bool IsProven => Failed is null;

    public OrderAbsenceProofInternal ToInternal()
    {
        return new OrderAbsenceProofInternal(Passed);
    }
}
