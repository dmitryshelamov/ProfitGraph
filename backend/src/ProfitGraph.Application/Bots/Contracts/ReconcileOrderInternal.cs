using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     An order to look up at the venue. <see cref="MayHaveReachedVenue" /> means a place request may have left the
///     process, so an empty lookup is not proof that the order does not exist.
/// </summary>
public sealed record ReconcileOrderInternal(long EngineOrderId, ClientOrderId ClientOrderId, bool MayHaveReachedVenue);