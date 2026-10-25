using MediatR;

using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData.DownloadCandles;

internal sealed class DownloadCandlesCommandHandler(CandleDownloadService downloads, MarketDataCredentials credentials)
    : IRequestHandler<DownloadCandlesCommand, HistoricalDownloadInternal>
{
    public async Task<HistoricalDownloadInternal> Handle(DownloadCandlesCommand request, CancellationToken cancellationToken)
    {
        using IDisposable access = MarketDataAccess.Use(
            await credentials.ReadIfStillThere(request.OwnerId ?? Guid.Empty, request.CredentialId, new VenueId(request.Venue), cancellationToken));

        return await downloads.Download(request, cancellationToken);
    }
}