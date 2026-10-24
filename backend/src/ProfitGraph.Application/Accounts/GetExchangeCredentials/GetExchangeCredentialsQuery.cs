using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.GetExchangeCredentials;

public sealed record GetExchangeCredentialsQuery(Guid OwnerId) : IQuery<IReadOnlyList<ExchangeCredentialInternal>>;