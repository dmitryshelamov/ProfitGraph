namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Whose Steam account a bot trades on and which item.
/// </summary>
internal sealed record SteamBotAccount(Guid BotId, Guid OwnerId, Guid CredentialId, SteamInstrument Instrument);