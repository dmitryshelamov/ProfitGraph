using FluentValidation;

using ProfitGraph.Application.MarketData;

namespace ProfitGraph.Application.Scanner.GetScanTradeIdeas;

internal sealed class GetScanTradeIdeasQueryValidator : AbstractValidator<GetScanTradeIdeasQuery>
{
    public GetScanTradeIdeasQueryValidator(VenueRegistry venues)
    {
        this.ValidSeries(venues, query => query.Venue, query => query.Symbol, _ => ScanTradeIdeasReader.PullbackTimeframe);
    }
}