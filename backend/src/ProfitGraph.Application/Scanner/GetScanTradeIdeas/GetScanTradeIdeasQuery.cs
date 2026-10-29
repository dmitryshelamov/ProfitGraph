using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanTradeIdeas;

/// <summary>
///     The trade cards of a scanner pair now (ADR 0060): the scenarios of the preset backtest from the current snapshot's price and
///     levels and the last hour's minutes, sized by the owner's settings when there are any.
/// </summary>
public sealed record GetScanTradeIdeasQuery(Guid OwnerId, string Venue, string Symbol) : IQuery<ScanTradeIdeasInternal>;