using FluentValidation;

using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.MarketData.StartCandleDownload;

internal sealed class StartCandleDownloadCommandValidator : AbstractValidator<StartCandleDownloadCommand>
{
    public StartCandleDownloadCommandValidator(VenueRegistry venues, VenuePorts<IHistoricalCandleSource> sources)
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