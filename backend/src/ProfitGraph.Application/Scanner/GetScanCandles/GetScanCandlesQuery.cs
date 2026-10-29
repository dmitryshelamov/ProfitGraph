using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanCandles;

/// <summary>
///     A page of a scanner pair's chart for the half-open UTC range [From, To) on any timeframe the venue offers (ADR 0059). The
///     venue must have a scanner; its market data is public. From is rounded down and To up to the timeframe grid, and To is cut
///     at the latest closed candle.
/// </summary>
public sealed record GetScanCandlesQuery(string Venue, string Symbol, string Timeframe, DateTimeOffset From, DateTimeOffset To) : IQuery<ScanCandlesInternal>;