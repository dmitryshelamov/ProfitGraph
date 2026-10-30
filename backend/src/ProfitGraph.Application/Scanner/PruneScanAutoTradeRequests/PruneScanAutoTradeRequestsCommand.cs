using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Scanner.PruneScanAutoTradeRequests;

/// <summary>
///     Deletes the auto trade requests older than <see cref="ScanAutoTradeOptions.RequestRetention" /> (ADR 0071, section 6).
/// </summary>
public sealed record PruneScanAutoTradeRequestsCommand : ICommand;