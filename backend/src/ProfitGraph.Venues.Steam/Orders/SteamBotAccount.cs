using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam.Trading;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Whose Steam account a bot trades on, which item, the seller fees of the bot's wallet that price its listings, and the
///     bot's own settings (ADR 0022, C9).
/// </summary>
internal sealed record SteamBotAccount(
    Guid BotId,
    Guid OwnerId,
    Guid CredentialId,
    SteamInstrument Instrument,
    SellerFeeSchedule? SellerFees = null,
    SteamBotSettings? Settings = null)
{
    public SteamBotSettings EffectiveSettings => Settings ?? SteamBotSettings.Defaults;
}