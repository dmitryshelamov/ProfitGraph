using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Steam;

/// <summary>
///     What a new Steam backtest starts from (ADR 0022, C4): Steam's 5% and the publisher's 10%, each at least one minor
///     unit. They only prefill the form, which shows them; a bot always reads the rates of its own wallet instead.
/// </summary>
internal sealed class SteamExecutionDefaults : IVenueExecutionDefaults
{
    public VenueId Venue => SteamVenue.VenueId;

    public VenueExecutionDefaultsInternal Defaults { get; } = new VenueExecutionDefaultsInternal(new SellerFeeScheduleInternal([0.05m, 0.10m], Minimum: 0.01m));
}