using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Trading.Contracts;

namespace ProfitGraph.Application.Trading.GetTradingHalt;

public sealed record GetTradingHaltQuery : IQuery<TradingHaltInternal>;