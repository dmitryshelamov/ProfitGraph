using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanTradeIdeas;

/// <summary>
///     The trade cards of a scanner pair now (ADR 0060): the scenarios of the preset backtest from the pair's price on the levels of
///     every timeframe and the last hour, sized by the owner's settings when there are any, each marked when a live trade already
///     took it.
/// </summary>
public sealed record GetScanTradeIdeasQuery(Guid OwnerId, string Venue, string Symbol) : IQuery<ScanTradeIdeasInternal>;