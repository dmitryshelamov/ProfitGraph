using MediatR;

using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.DownloadCandles;

internal sealed class DownloadCandlesCommandHandler(CandleDownloadService downloads) : IRequestHandler<DownloadCandlesCommand, HistoricalDownloadInternal>
{
    public Task<HistoricalDownloadInternal> Handle(DownloadCandlesCommand request, CancellationToken cancellationToken)
    {
        return downloads.Download(request, cancellationToken);
    }
}