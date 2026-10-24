using FluentValidation;

using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.MarketData.DownloadCandles;

internal sealed class DownloadCandlesCommandValidator : AbstractValidator<DownloadCandlesCommand>
{
    public DownloadCandlesCommandValidator(VenueRegistry venues, VenuePorts<IHistoricalCandleSource> sources)
    {
        this.ValidDownload(
            venues,
            sources,
            command => command.Venue,
            command => command.Symbol,
            command => command.Timeframe,
            command => command.From,
            command => command.To);
    }
}