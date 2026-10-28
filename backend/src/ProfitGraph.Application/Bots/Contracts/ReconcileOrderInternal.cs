using ProfitGraph.Domain.Bots.OrderIntents;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     An order to look up at the venue. <see cref="MayHaveReachedVenue" /> means a place request may have left the
///     process, so an empty lookup is not proof that the order does not exist. <see cref="OwnerConfirmedAbsent" /> means the
///     owner confirmed that it never reached the venue: a gateway that supports it looks the order up first and rejects it
///     only when the venue shows nothing. <see cref="ExecutedBase" /> is what the engine has already booked as executed for
///     the order and <see cref="PlaceStartedAt" /> when its place request first may have left the process; a gateway that
///     proves an absence by itself (ADR 0030) uses both, and <see cref="AcceptedByEngine" />, whether the engine saw the venue
///     accept the order, which a never-confirmed submit has not.
/// </summary>
public sealed record ReconcileOrderInternal(
    long EngineOrderId,
    ClientOrderId ClientOrderId,
    bool MayHaveReachedVenue,
    bool OwnerConfirmedAbsent = false,
    decimal ExecutedBase = 0m,
    DateTimeOffset? PlaceStartedAt = null,
    bool AcceptedByEngine = false);
