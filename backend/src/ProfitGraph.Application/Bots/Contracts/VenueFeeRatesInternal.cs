using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     Maker rate for limit orders and taker rate for market orders, as fractions.
/// </summary>
public sealed record VenueFeeRatesInternal(decimal MakerRate, decimal TakerRate, SellerFeeSchedule? SellerFees = null);