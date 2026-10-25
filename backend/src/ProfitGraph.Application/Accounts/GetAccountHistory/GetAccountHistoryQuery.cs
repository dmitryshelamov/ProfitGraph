using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.GetAccountHistory;

public sealed record GetAccountHistoryQuery(Guid OwnerId, Guid CredentialId, DateTimeOffset From, DateTimeOffset To, int Limit) : IQuery<AccountHistoryInternal>;