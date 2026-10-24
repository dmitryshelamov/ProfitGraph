using FluentValidation;

namespace ProfitGraph.Application.MarketData.ImportCandles;

/// <summary>
///     Checks what the import names before the stream is read; rows are checked one by one during the import.
/// </summary>
internal sealed class ImportCandlesCommandValidator : AbstractValidator<ImportCandlesCommand>
{
    public ImportCandlesCommandValidator(VenueRegistry venues)
    {
        this.ValidSeries(venues, command => command.Venue, command => command.Symbol, command => command.Timeframe);
        RuleFor(command => command.Candles).NotNull();
    }
}