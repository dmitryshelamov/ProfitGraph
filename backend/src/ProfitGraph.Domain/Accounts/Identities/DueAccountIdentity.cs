using ProfitGraph.Domain.Accounts.Credentials;

namespace ProfitGraph.Domain.Accounts.Identities;

/// <summary>
///     A credential whose account identity is due to be read, with its owner.
/// </summary>
public sealed record DueAccountIdentity(ExchangeCredentialId CredentialId, UserId OwnerId);