using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.DeleteExchangeCredential;

public sealed record DeleteExchangeCredentialCommand(Guid OwnerId, Guid Id) : ICommand;