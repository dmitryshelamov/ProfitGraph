using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.RecordAccountIdentity;

/// <summary>
///     Records one identity read of a credential's account (ADR 0049); a failure keeps the details of the last success.
/// </summary>
public sealed record RecordAccountIdentityCommand(Guid CredentialId, AccountIdentityReadInternal Read) : ICommand;