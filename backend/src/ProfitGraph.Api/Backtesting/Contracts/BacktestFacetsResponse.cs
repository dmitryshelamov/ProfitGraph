namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     The values of the run list's filters, most runs first. Quotes count completed runs with a result; the other
///     dimensions count every finished run. Statuses are <c>completed</c>, <c>failed</c> and <c>cancelled</c>; origins are
///     <c>manual</c> and <c>optimization</c>.
/// </summary>
public sealed record BacktestFacetsResponse(
    IReadOnlyList<BacktestFacetResponse> Quotes,
    IReadOnlyList<BacktestFacetResponse> Statuses,
    IReadOnlyList<BacktestFacetResponse> Origins,
    IReadOnlyList<BacktestFacetResponse> Venues,
    IReadOnlyList<BacktestFacetResponse> Timeframes,
    IReadOnlyList<BacktestStrategyFacetResponse> Strategies);