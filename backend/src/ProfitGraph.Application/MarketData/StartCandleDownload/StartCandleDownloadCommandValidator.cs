using FluentValidation;

using ProfitGraph.Application.MarketData.DownloadCandles;

namespace ProfitGraph.Application.MarketData.StartCandleDownload;

internal sealed class StartCandleDownloadCommandValidator : AbstractValidator<StartCandleDownloadCommand>
{
    public StartCandleDownloadCommandValidator(VenueRegistry venues, HistorySources sources)
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