using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.TakeScanAutoTradeCard;

/// <summary>
///     Finishes a claimed auto trade request with the pair's cards as its page shows them (ADR 0071, section 6): the rule's card is
///     taken when its odds reach the rule's minimums.
/// </summary>
public sealed record TakeScanAutoTradeCardCommand(Guid RequestId, ScanTradeIdeasInternal Ideas) : ICommand;