using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.RenameExchangeCredential;

/// <summary>
///     Renames the owner's credential; its secrets, venue account and bots stay as they are.
/// </summary>
public sealed record RenameExchangeCredentialCommand(Guid OwnerId, Guid Id, string? Label) : ICommand<ExchangeCredentialInternal>;