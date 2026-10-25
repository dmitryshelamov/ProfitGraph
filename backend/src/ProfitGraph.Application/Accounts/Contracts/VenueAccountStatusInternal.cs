namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     A venue account's status by its code, and when the venue checks it next, if it will.
/// </summary>
public sealed record VenueAccountStatusInternal(string Status, DateTimeOffset? NextCheckAt);