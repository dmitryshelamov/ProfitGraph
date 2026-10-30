using MediatR;

using ProfitGraph.Application.Scanner.ClaimScanAutoTradeRequest;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.GetScanTradeIdeas;
using ProfitGraph.Application.Scanner.TakeScanAutoTradeCard;

namespace ProfitGraph.Application.Scanner.AutoTrades;

/// <summary>
///     Handles one auto trade request in three steps (ADR 0071, section 6): claims it in a transaction, reads the pair's cards as its
///     page does outside any transaction, since that reads the venue, and finishes it in a second transaction. A request whose cards
///     cannot be read stays claimed and expires.
/// </summary>
public sealed class ScanAutoTradeRequestHandler(ISender sender)
{
    /// <summary>
    ///     Answers whether there was a request to handle.
    /// </summary>
    public async Task<bool> HandleNext(CancellationToken cancellationToken)
    {
        if (await sender.Send(new ClaimScanAutoTradeRequestCommand(), cancellationToken) is not { } claim)
        {
            return false;
        }

        if (claim.Claimed)
        {
            ScanTradeIdeasInternal ideas = await sender.Send(new GetScanTradeIdeasQuery(claim.OwnerId, claim.Venue, claim.Symbol), cancellationToken);
            await sender.Send(new TakeScanAutoTradeCardCommand(claim.RequestId, ideas), cancellationToken);
        }

        return true;
    }
}