using FluentValidation;

using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData.ImportCandles;

/// <summary>
///     Checks one imported candle: consistent OHLCV on the timeframe grid, and closed by the time of the import.
/// </summary>
internal sealed class CandleImportRowValidator : AbstractValidator<CandleImportRowCheckInternal>
{
    public CandleImportRowValidator()
    {
        RuleFor(check => check)
            .Custom((check, context) =>
            {
                Candle candle = check.Row.Candle;
                var field = $"File.Line[{check.Row.LineNumber}]";

                if (!CandleValidator.IsValid(candle, check.Timeframe))
                {
                    context.AddFailure(field, "OHLCV must be positive and consistent, with nonnegative volume and a UTC open time on the timeframe grid.");
                }
                else if (candle.OpenTime.Add(check.Timeframe.Duration) > check.Now)
                {
                    context.AddFailure(field, "Only complete candles can be imported: the candle closes in the future.");
                }
            });
    }
}