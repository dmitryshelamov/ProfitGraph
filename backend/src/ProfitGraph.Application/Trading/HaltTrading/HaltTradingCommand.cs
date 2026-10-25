using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Trading.Contracts;

namespace ProfitGraph.Application.Trading.HaltTrading;

/// <summary>
///     The Owner's global kill switch (Profit Fox Designer ADR 0056): every submit and start is blocked and every bot that
///     should run is killed.
/// </summary>
public sealed record HaltTradingCommand : ICommand<TradingHaltInternal>;