namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     One item in the mock account's inventory with the time its trade hold ends.
/// </summary>
internal sealed record MockSteamMarketAsset(string Id, DateTimeOffset UnlockAt);