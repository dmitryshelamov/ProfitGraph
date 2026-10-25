using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.MarketData.Contracts;
using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Api.MarketData.Converters;

public static class MarketDataResponseConverter
{
    public static VenueResponse ToResponse(this VenueInternal venue)
    {
        return new VenueResponse(
            venue.Id,
            venue.Name,
            venue.Timeframes,
            venue.ExecutionDefaults is { } defaults
                ? new VenueExecutionDefaultsResponse(
                    defaults.SellerFees is { } fees
                        ? new SellerFeeScheduleResponse([.. fees.Rates.Select(ExactGraphNumberParser.Format)], ExactGraphNumberParser.Format(fees.Minimum))
                        : null,
                    defaults.SettlementHoldSeconds)
                : null);
    }

    public static CandleResponse ToResponse(this CandleInternal candle)
    {
        return new CandleResponse(
            candle.OpenTime,
            ExactDecimalText.Format(candle.Open),
            ExactDecimalText.Format(candle.High),
            ExactDecimalText.Format(candle.Low),
            ExactDecimalText.Format(candle.Close),
            ExactDecimalText.Format(candle.Volume));
    }

    public static CandleImportResponse ToResponse(this CandleImportInternal import)
    {
        return new CandleImportResponse(import.Venue, import.Symbol, import.Timeframe, import.Accepted, import.From, import.To);
    }

    public static InstrumentResponse ToResponse(this InstrumentInternal instrument)
    {
        return new InstrumentResponse(instrument.Venue, instrument.Symbol, instrument.BaseAsset, instrument.QuoteAsset);
    }
}