using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.ReadExchangeCredentialSecrets;

/// <summary>
///     Decrypts a credential for a venue gateway in the live runtime. No HTTP endpoint sends this query.
/// </summary>
public sealed record ReadExchangeCredentialSecretsQuery(Guid OwnerId, Guid Id) : IQuery<ExchangeCredentialSecretsInternal>;