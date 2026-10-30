using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanAutoTrade;

/// <summary>
///     The auto trades of the user's own preset (ADR 0071): its rule, today's tally, the backtests of its conditions with their kinds of
///     cards at the given minimums (the rule's when omitted, else an expected R of 0.1), its latest requests and paper trades.
/// </summary>
public sealed record GetScanAutoTradeQuery(Guid UserId, Guid PresetId, decimal? MinExpectedR = null, decimal? MinChance = null) : IQuery<ScanAutoTradeInternal>;