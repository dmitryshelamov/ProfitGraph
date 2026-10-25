using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Trading.Contracts;

namespace ProfitGraph.Application.Trading.ResumeTrading;

/// <summary>
///     Clears the global halt; no bot starts by itself.
/// </summary>
public sealed record ResumeTradingCommand : ICommand<TradingHaltInternal>;