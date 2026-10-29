using System.Text.RegularExpressions;

using FluentValidation;

using ProfitGraph.Application.Common.Queries;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Backtesting.GetBacktestRanking;

internal sealed class GetBacktestRankingQueryValidator : AbstractValidator<GetBacktestRankingQuery>
{
    private static readonly Regex QuotePattern = new Regex("^[A-Z0-9]{1,32}$", RegexOptions.CultureInvariant);

    public GetBacktestRankingQueryValidator(QueryLimitOptions limits)
    {
        RuleFor(query => query.Filter.Status).IsInEnum();
        RuleFor(query => query.Filter.Quote)
            .Must(quote => quote is not null && QuotePattern.IsMatch(quote))
            .When(query => query.Filter.Status == BacktestRankingStatus.Completed || query.Filter.Quote is not null)
            .WithErrorCode("invalid_quote")
            .WithMessage("Give an upper-case quote asset.");
        RuleFor(query => query.Filter.Venue)
            .Matches("^[a-z0-9-]{1,32}$")
            .When(query => query.Filter.Venue is not null)
            .WithErrorCode("invalid_venue")
            .WithMessage("Give a venue identifier.");
        RuleFor(query => query.Filter.Symbol).MaximumLength(InstrumentSymbol.MaxLength).When(query => query.Filter.Symbol is not null);
        RuleFor(query => query.Filter.Search).MaximumLength(Math.Max(InstrumentSymbol.MaxLength, StrategyName.MaxLength)).When(query => query.Filter.Search is not null);
        RuleFor(query => query.Filter.Timeframe)
            .Must(IsSupported)
            .When(query => query.Filter.Timeframe is not null)
            .WithErrorCode("unsupported_timeframe")
            .WithMessage("The timeframe is not supported.");
        RuleFor(query => query.Filter.Origin).IsInEnum().When(query => query.Filter.Origin is not null);
        RuleFor(query => query.Filter.To)
            .GreaterThan(query => query.Filter.From)
            .When(query => query.Filter is { From: not null, To: not null })
            .WithErrorCode("invalid_period")
            .WithMessage("The period ends after it starts.");
        RuleFor(query => query.Sort)
            .Equal(BacktestRankingSort.CreatedAt)
            .When(query => query.Filter.Status != BacktestRankingStatus.Completed)
            .WithErrorCode("sort_needs_result")
            .WithMessage("Failed and cancelled runs have no result; sort them by start time.");
        RuleFor(query => query.Filter)
            .Must(filter => filter.MinReturnPercent is null && filter.MaxDrawdownPercent is null)
            .When(query => query.Filter.Status != BacktestRankingStatus.Completed)
            .WithErrorCode("result_filter_needs_result")
            .WithMessage("Failed and cancelled runs have no result to filter by.");
        RuleFor(query => query.Sort).IsInEnum();
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.BacktestRanking);
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