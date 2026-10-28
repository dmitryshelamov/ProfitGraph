using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Backtesting.GetBacktestObservations;

/// <summary>
///     Candle observations after a sequence, with their facts.
/// </summary>
public sealed record GetBacktestObservationsQuery(Guid UserId, Guid Id, long After, int Limit) : IQuery<BacktestPageInternal<BacktestObservationInternal>>;