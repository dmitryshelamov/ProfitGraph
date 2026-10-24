using ProfitGraph.Engine.Common;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Frozen run preparation metadata cannot be changed or advanced inconsistently.
/// </summary>
public sealed class BacktestRunInvariantException(string message) : EngineException(message);