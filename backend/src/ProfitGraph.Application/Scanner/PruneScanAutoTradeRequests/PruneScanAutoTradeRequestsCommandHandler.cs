using MediatR;

using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.PruneScanAutoTradeRequests;

internal sealed class PruneScanAutoTradeRequestsCommandHandler(IScanAutoTradeRequestRepository requests, ScanAutoTradeOptions options, TimeProvider clock)
    : IRequestHandler<PruneScanAutoTradeRequestsCommand>
{
    public Task Handle(PruneScanAutoTradeRequestsCommand command, CancellationToken cancellationToken)
    {
        return requests.DeleteBefore(clock.GetUtcNow() - options.RequestRetention, cancellationToken);
    }
}