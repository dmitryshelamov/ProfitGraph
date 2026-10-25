namespace ProfitGraph.Api.Accounts.Contracts;

/// <summary>
///     A venue account's status by the venue's own code, such as an authenticator still being checked or ready, and when the
///     venue checks it next.
/// </summary>
public sealed record AccountStatusResponse(string Status, DateTimeOffset? NextCheckAt);