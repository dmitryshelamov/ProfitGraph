using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.GetAuthenticatorCode;

public sealed record GetAuthenticatorCodeQuery(Guid OwnerId, Guid CredentialId) : IQuery<AuthenticatorCodeInternal>;