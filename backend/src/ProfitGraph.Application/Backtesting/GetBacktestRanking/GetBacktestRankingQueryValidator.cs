using FluentValidation;

using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.Backtesting.GetBacktestRanking;

internal sealed class GetBacktestRankingQueryValidator : AbstractValidator<GetBacktestRankingQuery>
{
    public GetBacktestRankingQueryValidator()
    {
        RuleFor(query => query.Quote).Matches("^[A-Z0-9]{1,32}$").WithErrorCode("invalid_quote").WithMessage("Give an upper-case quote asset.");
        RuleFor(query => query.Symbol).MaximumLength(64).When(query => query.Symbol is not null);
        RuleFor(query => query.Timeframe)
            .Must(IsSupported)
            .When(query => query.Timeframe is not null)
            .WithErrorCode("unsupported_timeframe")
            .WithMessage("The timeframe is not supported.");
        RuleFor(query => query.Sort).IsInEnum();
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, GetBacktestRankingQuery.MaxLimit);
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