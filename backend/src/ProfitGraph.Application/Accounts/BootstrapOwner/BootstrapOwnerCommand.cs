using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.BootstrapOwner;

/// <summary>
///     Creates the first Owner from private startup configuration when no account exists; returns whether it was created.
/// </summary>
public sealed record BootstrapOwnerCommand(string Login, string Password) : ICommand<bool>;