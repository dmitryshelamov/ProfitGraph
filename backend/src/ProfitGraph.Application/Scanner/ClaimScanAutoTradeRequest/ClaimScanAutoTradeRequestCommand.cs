using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.ClaimScanAutoTradeRequest;

/// <summary>
///     Takes up the oldest pending auto trade request no other Worker holds (ADR 0071, section 6): one that may not trade now is skipped
///     with its reason, any other is claimed for its cards to be read outside the transaction; null when none is pending.
/// </summary>
public sealed record ClaimScanAutoTradeRequestCommand : ICommand<ScanAutoTradeClaimInternal?>;