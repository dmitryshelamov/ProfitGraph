using FluentValidation;
using FluentValidation.Results;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.Backtesting.StartBacktest;

internal sealed class StartBacktestCommandValidator : AbstractValidator<StartBacktestCommand>
{
    private const int MaxParameters = 100;
    private const int MaxSellerFeeComponents = 4;
    private const decimal MaxStartingCapital = 1_000_000_000_000m;

    public StartBacktestCommandValidator(VenueRegistry venues, VenuePorts<IInstrumentRulesSource> rules)
    {
        RuleFor(command => command.Version).GreaterThan(0).When(command => command.Version is not null);
        this.ValidSeries(venues, command => command.Venue, command => command.Symbol, command => command.Timeframe);
        RuleFor(command => command)
            .Custom((command, context) =>
            {
                if (venues.TryResolveSeries(command.Venue, command.Symbol, command.Timeframe, out CandleSeries? series).Count > 0)
                {
                    return;
                }

                if (rules.Find(series!.Venue) is null)
                {
                    context.AddFailure(
                        new ValidationFailure("Venue", "This venue offers no instrument rules to replay against.")
                            { ErrorCode = InstrumentRulesErrorCodes.InvalidRequest });
                }

                foreach ((string field, DateTimeOffset? bound) in new[] { ("From", command.From), ("To", command.To) })
                {
                    if (bound is { } value && (value.Offset != TimeSpan.Zero || value < DateTimeOffset.UnixEpoch || !series.Timeframe.IsAligned(value)))
                    {
                        context.AddFailure(
                            new ValidationFailure(field, "Use a UTC time on the timeframe grid, at or after 1970-01-01.") { ErrorCode = "invalid_request" });
                    }
                }

                if (command.From is not null && command.To is not null && command.To <= command.From)
                {
                    context.AddFailure(new ValidationFailure("To", "The end must come after the start.") { ErrorCode = "invalid_request" });
                }
            });
        RuleFor(command => command.Parameters)
            .Must(parameters => parameters is null || (parameters.Count <= MaxParameters && parameters.Keys.All(name => !string.IsNullOrWhiteSpace(name))))
            .WithMessage($"Give at most {MaxParameters} parameters by name.");
        RuleFor(command => command.StartingCapital).GreaterThan(0m).LessThanOrEqualTo(MaxStartingCapital);
        RuleFor(command => command.Slippage).InclusiveBetween(from: 0m, to: 0.5m);
        RuleFor(command => command.LimitFeeRate).InclusiveBetween(from: 0m, to: 0.5m);
        RuleFor(command => command.MarketFeeRate).InclusiveBetween(from: 0m, to: 0.5m);
        RuleFor(command => command.SellerFees)
            .Must(fees => fees!.Rates.Count is > 0 and <= MaxSellerFeeComponents && fees.Rates.All(rate => rate is >= 0m and <= 0.5m) && fees.Minimum >= 0m)
            .WithMessage($"Seller fees need 1 to {MaxSellerFeeComponents} rates in [0, 0.5] and a nonnegative minimum.")
            .Must((command, _) => command.LimitFeeRate == 0m && command.MarketFeeRate == 0m)
            .WithMessage("Seller fees replace the limit and market rates; leave those at zero.")
            .When(command => command.SellerFees is not null);
        RuleFor(command => command.OutOfSampleFraction).InclusiveBetween(from: 0m, to: 0.9m).When(command => command.OutOfSampleFraction is not null);
    }
}