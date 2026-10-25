using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.StartBacktest;
using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Api.Backtesting.Converters;

public static class StartBacktestRequestConverter
{
    public static StartBacktestCommand ToCommand(this StartBacktestRequest request, Guid userId)
    {
        var errors = new List<InputError>();
        Dictionary<string, decimal>? parameters = request.Parameters?.ToDictionary(
            pair => pair.Key,
            pair => Number(pair.Value, $"Parameters.{pair.Key}", errors) ?? 0m,
            StringComparer.Ordinal);
        decimal? startingCapital = Number(request.StartingCapital, "StartingCapital", errors);
        decimal? slippage = Number(request.Slippage ?? "0", "Slippage", errors);
        decimal? limitFeeRate = Number(request.LimitFeeRate ?? "0", "LimitFeeRate", errors);
        decimal? marketFeeRate = Number(request.MarketFeeRate ?? "0", "MarketFeeRate", errors);
        decimal? outOfSampleFraction = request.OutOfSampleFraction is null ? null : Number(request.OutOfSampleFraction, "OutOfSampleFraction", errors);
        SellerFeeScheduleInternal? sellerFees = request.SellerFees is { } fees
            ? new SellerFeeScheduleInternal(
                (fees.Rates ?? []).Select((rate, index) => Number(rate, $"SellerFees.Rates[{index}]", errors) ?? 0m).ToArray(),
                Number(fees.Minimum, "SellerFees.Minimum", errors) ?? 0m)
            : null;

        if (errors.Count > 0)
        {
            throw new ApplicationValidationException(errors);
        }

        return new StartBacktestCommand(
            userId,
            request.StrategyId,
            request.Version,
            request.Venue ?? string.Empty,
            request.Symbol ?? string.Empty,
            request.Timeframe ?? string.Empty,
            request.From,
            request.To,
            parameters,
            startingCapital!.Value,
            slippage!.Value,
            limitFeeRate!.Value,
            marketFeeRate!.Value,
            outOfSampleFraction,
            sellerFees);
    }

    private static decimal? Number(string? value, string field, List<InputError> errors)
    {
        if (value is not null && ExactGraphNumberParser.TryDecimal(value, out decimal number))
        {
            return number;
        }

        errors.Add(new InputError("invalid_number", field, "Use an exact invariant decimal string."));

        return null;
    }
}