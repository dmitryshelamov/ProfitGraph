namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     One pending mobile confirmation of the mock account: 12 for a buy order, 3 for a listing.
/// </summary>
internal sealed record MockSteamMarketConfirmation(string Id, string Nonce, int Type, string CreatorId);