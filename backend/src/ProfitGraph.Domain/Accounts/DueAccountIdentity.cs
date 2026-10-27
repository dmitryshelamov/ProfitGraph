namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     A credential whose account identity is due to be read, with its owner.
/// </summary>
public sealed record DueAccountIdentity(ExchangeCredentialId CredentialId, UserId OwnerId);