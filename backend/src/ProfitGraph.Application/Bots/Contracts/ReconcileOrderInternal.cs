using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     An order to look up at the venue. <see cref="MayHaveReachedVenue" /> means a place request may have left the
///     process, so an empty lookup is not proof that the order does not exist. <see cref="OwnerConfirmedAbsent" /> means the
///     owner confirmed that it never reached the venue: a gateway that supports it looks the order up first and rejects it
///     only when the venue shows nothing.
/// </summary>
public sealed record ReconcileOrderInternal(long EngineOrderId, ClientOrderId ClientOrderId, bool MayHaveReachedVenue, bool OwnerConfirmedAbsent = false);