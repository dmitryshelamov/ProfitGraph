using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestFacets;

/// <summary>
///     The filter values of the user's finished runs, for the run list's filters.
/// </summary>
public sealed record GetBacktestFacetsQuery(Guid UserId) : IQuery<BacktestFacetsInternal>;