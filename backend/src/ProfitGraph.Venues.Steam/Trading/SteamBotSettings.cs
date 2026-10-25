namespace ProfitGraph.Venues.Steam.Trading;

/// <summary>
///     A Steam bot's own settings, version 1 (ADR 0022, C9): the trade hold it expects after a buy, shown only, since each
///     item's own unlock time decides; how far above the lowest listing a market buy may pay; and how long a market buy's
///     order stays before its remainder is cancelled.
/// </summary>
internal sealed record SteamBotSettings(int SettlementHoldDays, decimal MarketBuySlippage, int MarketBuyDeadlineSeconds)
{
    public const int Version = 1;

    public static readonly SteamBotSettings Defaults = new SteamBotSettings(SettlementHoldDays: 7, MarketBuySlippage: 0m, MarketBuyDeadlineSeconds: 120);
}