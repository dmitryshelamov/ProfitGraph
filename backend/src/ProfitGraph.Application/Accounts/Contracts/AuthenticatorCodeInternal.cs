namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     An authenticator's current code and when it stops being accepted.
/// </summary>
public sealed record AuthenticatorCodeInternal(string Code, DateTimeOffset ValidUntil);