using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanTrades;

/// <summary>
///     The owner's latest scanner trades on a venue (ADR 0060), newest first; one pair's when <see cref="Symbol" /> is given.
/// </summary>
public sealed record GetScanTradesQuery(Guid OwnerId, string Venue, string? Symbol) : IQuery<IReadOnlyList<ScanTradeInternal>>;