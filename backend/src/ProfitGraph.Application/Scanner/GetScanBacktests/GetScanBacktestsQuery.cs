using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanBacktests;

/// <summary>
///     The user's latest scanner backtests on the venue, newest first.
/// </summary>
public sealed record GetScanBacktestsQuery(Guid UserId, string Venue) : IQuery<IReadOnlyList<ScanBacktestListItemInternal>>;