using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Whose Steam account a bot trades on, which item, and the seller fees of the bot's wallet that price its listings.
/// </summary>
internal sealed record SteamBotAccount(Guid BotId, Guid OwnerId, Guid CredentialId, SteamInstrument Instrument, SellerFeeSchedule? SellerFees = null);