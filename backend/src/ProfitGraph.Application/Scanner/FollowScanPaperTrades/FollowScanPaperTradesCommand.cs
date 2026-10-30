using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.FollowScanPaperTrades;

/// <summary>
///     Steps the venue's open paper trades through the closed minutes the scanner holds of their pairs (ADR 0071, section 7), after
///     each snapshot.
/// </summary>
public sealed record FollowScanPaperTradesCommand(string Venue, IReadOnlyDictionary<string, IReadOnlyList<ScanCandle>> Minutes) : ICommand;