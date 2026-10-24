namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     What a user may see of a stored credential; never a secret.
/// </summary>
public sealed record ExchangeCredentialInternal(Guid Id, string Venue, string Label, string ApiKeyLast4, DateTimeOffset CreatedAt);