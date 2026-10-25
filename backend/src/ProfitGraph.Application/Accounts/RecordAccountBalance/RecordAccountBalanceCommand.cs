using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.RecordAccountBalance;

/// <summary>
///     Records one balance read of a credential: its status and, for an observation, its snapshot, in one transaction. A
///     failure keeps the last snapshot and success time; a second observation in the same five-minute bucket is dropped.
/// </summary>
public sealed record RecordAccountBalanceCommand(Guid CredentialId, AccountBalanceReadInternal Read) : ICommand<bool>;