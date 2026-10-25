namespace ProfitGraph.Api.Accounts.Contracts;

/// <summary>
///     An authenticator's current code, made on request and never stored, and when it stops being accepted.
/// </summary>
public sealed record AuthenticatorCodeResponse(string Code, DateTimeOffset ValidUntil);