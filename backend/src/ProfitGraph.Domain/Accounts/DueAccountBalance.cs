namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     A credential whose account balance is due to be read, with its owner.
/// </summary>
public sealed record DueAccountBalance(ExchangeCredentialId CredentialId, UserId OwnerId);