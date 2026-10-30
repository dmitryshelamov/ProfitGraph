using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     The costs a pair's cards are sized with (ADR 0065): <see cref="FeesFromVenue" /> says the fee rates are the venue's for the owner's
///     credential rather than <see cref="ScanTradeOptions.FeeRate" />, and <see cref="SpreadKnown" /> that the spread comes from the
///     snapshot rather than being taken as zero.
/// </summary>
public sealed record ScanTradeCostsInternal(ScanTradeCosts Costs, bool FeesFromVenue, bool SpreadKnown);