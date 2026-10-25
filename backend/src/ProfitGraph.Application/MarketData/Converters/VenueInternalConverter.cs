using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;

namespace ProfitGraph.Application.MarketData.Converters;

public static class VenueInternalConverter
{
    public static VenueInternal ToInternal(this IVenueDescriptor venue, VenueExecutionDefaultsInternal? executionDefaults, string? botSettings = null)
    {
        return new VenueInternal(
            venue.Id.Value,
            venue.Name,
            [.. venue.Timeframes.Select(timeframe => timeframe.Key)],
            executionDefaults,
            botSettings,
            venue.BacktestLimits);
    }
}