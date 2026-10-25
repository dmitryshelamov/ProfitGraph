using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Steam;

/// <summary>
///     What a new Steam backtest starts from (ADR 0022, C4): Steam's 5% and the publisher's 10%, each at least one minor
///     unit, and a seven-day trade hold, an estimate rather than a Steam rule. They only prefill the form, which shows them;
///     a bot reads the rates of its own wallet and the unlock times of its own items instead.
/// </summary>
internal sealed class SteamExecutionDefaults : IVenueExecutionDefaults
{
    private const long SevenDays = 7 * 24 * 60 * 60;
    public VenueId Venue => SteamVenue.VenueId;

    public VenueExecutionDefaultsInternal Defaults { get; } =
        new VenueExecutionDefaultsInternal(new SellerFeeScheduleInternal([0.05m, 0.10m], Minimum: 0.01m), SevenDays);
}