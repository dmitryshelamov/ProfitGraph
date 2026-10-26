namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     What a user may see of a stored credential; never a secret. <paramref name="PublicId" /> is its non-secret identifier, null when none can be read.
/// </summary>
public sealed record ExchangeCredentialInternal(Guid Id, string Venue, string Label, string ApiKeyLast4, string? PublicId, DateTimeOffset CreatedAt);