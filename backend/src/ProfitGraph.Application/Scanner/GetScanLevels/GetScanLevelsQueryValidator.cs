using FluentValidation;

using ProfitGraph.Application.MarketData;

namespace ProfitGraph.Application.Scanner.GetScanLevels;

internal sealed class GetScanLevelsQueryValidator : AbstractValidator<GetScanLevelsQuery>
{
    public GetScanLevelsQueryValidator(VenueRegistry venues)
    {
        this.ValidSeries(venues, query => query.Venue, query => query.Symbol, query => query.Timeframe);
    }
}