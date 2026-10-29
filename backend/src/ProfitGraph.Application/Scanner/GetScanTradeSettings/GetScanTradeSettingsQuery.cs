using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanTradeSettings;

/// <summary>
///     The owner's trade card settings on a scanner venue (ADR 0060), or null before they are set.
/// </summary>
public sealed record GetScanTradeSettingsQuery(Guid OwnerId, string Venue) : IQuery<ScanTradeSettingsInternal?>;