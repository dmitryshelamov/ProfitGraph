using ProfitGraph.Engine.Common;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Exact valuation at the reached price cannot be represented by decimal; committed balances remain unchanged.
/// </summary>
public sealed class BacktestValuationException(OverflowException innerException)
    : EngineException("Account valuation at the reached price exceeds the decimal range.", innerException);