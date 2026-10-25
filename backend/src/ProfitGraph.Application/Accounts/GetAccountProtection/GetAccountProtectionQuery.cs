using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.GetAccountProtection;

public sealed record GetAccountProtectionQuery(Guid OwnerId, Guid CredentialId) : IQuery<VenueAccountProtectionInternal>;