using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.GetAccountStatus;

public sealed record GetAccountStatusQuery(Guid OwnerId, Guid CredentialId) : IQuery<VenueAccountStatusInternal>;