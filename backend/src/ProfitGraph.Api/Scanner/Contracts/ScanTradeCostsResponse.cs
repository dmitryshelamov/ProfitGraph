namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     The costs a pair's cards are sized with (ADR 0065), as fractions in exact strings: the fee of the entry's and the target's limit
///     orders, the fee of the stop's market order and the spread it crosses; whether the fees are the venue's for the owner's credential
///     and whether the spread comes from the snapshot.
/// </summary>
public sealed record ScanTradeCostsResponse(string LimitFeeRate, string MarketFeeRate, string Spread, bool FeesFromVenue, bool SpreadKnown);