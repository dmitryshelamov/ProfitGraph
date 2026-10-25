using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     Invented seller fees as the test venue's backtest defaults (ADR 0022, C4).
/// </summary>
public sealed class TestExecutionDefaults : IVenueExecutionDefaults
{
    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public VenueExecutionDefaultsInternal Defaults { get; } = new VenueExecutionDefaultsInternal(
        new SellerFeeScheduleInternal([0.05m, 0.10m], Minimum: 0.01m),
        SettlementHoldSeconds: null);
}