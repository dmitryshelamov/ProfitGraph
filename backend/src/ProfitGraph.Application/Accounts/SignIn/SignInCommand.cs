using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.SignIn;

/// <summary>
///     Checks credentials. Returns null for a wrong password, an unknown login or a blocked account alike.
/// </summary>
public sealed record SignInCommand(string Login, string Password) : ICommand<UserContract?>;