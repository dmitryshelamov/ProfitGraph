namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     The evidence on which a gateway closed an order the venue no longer knows, without the owner (ADR 0030): the stable
///     codes of the checks that all held, in the order they were made. A code names a check and holds no account data.
/// </summary>
public sealed record OrderAbsenceProofInternal(IReadOnlyList<string> Checks);
