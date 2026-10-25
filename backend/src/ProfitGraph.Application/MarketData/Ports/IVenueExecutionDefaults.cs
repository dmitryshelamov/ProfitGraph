using ProfitGraph.Application.MarketData.Contracts;

namespace ProfitGraph.Application.MarketData.Ports;

/// <summary>
///     A venue's defaults for a new backtest, such as its marketplace fees (ADR 0022, C4). They only prefill the request:
///     a run stores what the request gave, so a later change of the defaults never changes a run. A venue without this port
///     has no defaults.
/// </summary>
public interface IVenueExecutionDefaults : IVenuePort
{
    VenueExecutionDefaultsInternal Defaults { get; }
}