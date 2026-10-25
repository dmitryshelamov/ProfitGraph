using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     A signed-in account's wallet as the market page reports it: its currency, balance and the seller fees of a sale.
/// </summary>
internal sealed record SteamWallet(SteamCurrency Currency, decimal Balance, SellerFeeSchedule SellerFees);