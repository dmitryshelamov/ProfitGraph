namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     A signed-in user with a strategy and an invented credential, ready to create bots.
/// </summary>
internal sealed record BotOwner(HttpClient Client, Guid StrategyId, Guid CredentialId);