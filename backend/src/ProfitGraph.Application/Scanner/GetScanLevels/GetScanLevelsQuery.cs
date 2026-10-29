using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanLevels;

/// <summary>
///     A scanner pair's support, resistance and reward-to-risk on a chosen timeframe (ADR 0059, section 8): the levels of its last
///     closed candles of that timeframe at its current price. The venue must have a scanner; its market data is public.
/// </summary>
public sealed record GetScanLevelsQuery(string Venue, string Symbol, string Timeframe) : IQuery<ScanLevelsInternal>;