using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.ResumeAccountProtection;

/// <summary>
///     The owner resumes a venue account's protection after looking at the account; its bots run again and reconcile first.
/// </summary>
public sealed record ResumeAccountProtectionCommand(Guid OwnerId, Guid CredentialId) : ICommand;