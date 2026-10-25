namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     An account at a venue that a credential may select (ADR 0023, TC5): its identifier, the venue's name and kind for it,
///     and whether a bot may trade through it, with the reason when not.
/// </summary>
public sealed record VenueAccountInternal(string Id, string Name, string Kind, bool Eligible, string? Ineligibility);