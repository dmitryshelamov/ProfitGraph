using FluentValidation;

namespace ProfitGraph.Application.Bots.GetBotCandles;

internal sealed class GetBotCandlesQueryValidator : AbstractValidator<GetBotCandlesQuery>
{
    public GetBotCandlesQueryValidator()
    {
        RuleFor(query => query.From).Must(value => value.Offset == TimeSpan.Zero).WithErrorCode("invalid_value").WithMessage("Use a UTC time.");
        RuleFor(query => query.To)
            .Must(value => value.Offset == TimeSpan.Zero)
            .WithErrorCode("invalid_value")
            .WithMessage("Use a UTC time.")
            .GreaterThan(query => query.From)
            .WithErrorCode("invalid_value")
            .WithMessage("The end must come after the start.");
    }
}