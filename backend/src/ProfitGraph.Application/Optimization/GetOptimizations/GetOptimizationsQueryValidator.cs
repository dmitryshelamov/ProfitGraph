using FluentValidation;

using ProfitGraph.Application.Common.Queries;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Optimization.GetOptimizations;

internal sealed class GetOptimizationsQueryValidator : AbstractValidator<GetOptimizationsQuery>
{
    public GetOptimizationsQueryValidator(QueryLimitOptions limits)
    {
        RuleFor(query => query.Filter.State).IsInEnum().When(query => query.Filter.State is not null);
        RuleFor(query => query.Filter.Method).IsInEnum().When(query => query.Filter.Method is not null);
        RuleFor(query => query.Filter.Venue)
            .Matches("^[a-z0-9-]{1,32}$")
            .When(query => query.Filter.Venue is not null)
            .WithErrorCode("invalid_venue")
            .WithMessage("Give a venue identifier.");
        RuleFor(query => query.Filter.Search).MaximumLength(Math.Max(InstrumentSymbol.MaxLength, StrategyName.MaxLength)).When(query => query.Filter.Search is not null);
        RuleFor(query => query.Filter.Timeframe)
            .Must(IsSupported)
            .When(query => query.Filter.Timeframe is not null)
            .WithErrorCode("unsupported_timeframe")
            .WithMessage("The timeframe is not supported.");
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.Optimizations);
    }

    private static bool IsSupported(string? timeframe)
    {
        try
        {
            Timeframe.Parse(timeframe!);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}