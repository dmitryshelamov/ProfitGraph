using ProfitGraph.Domain.Bots.OrderIntents;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     An order the venue reported during reconciliation, and whether it can no longer change.
///     <see cref="AbsenceProof" /> is set when the gateway closed it without the owner because the venue no longer knows it
///     (ADR 0030).
/// </summary>
public sealed record ReconciledOrderInternal(ClientOrderId ClientOrderId, bool IsFinal, OrderAbsenceProofInternal? AbsenceProof = null);
