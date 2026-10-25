using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     An order the venue reported during reconciliation, and whether it can no longer change.
/// </summary>
public sealed record ReconciledOrderInternal(ClientOrderId ClientOrderId, bool IsFinal);