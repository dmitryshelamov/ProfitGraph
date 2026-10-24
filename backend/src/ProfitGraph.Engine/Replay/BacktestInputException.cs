using ProfitGraph.Engine.Common;
using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Preserves referenced selection diagnostics for preflight before account or history-provider work.
/// </summary>
public sealed class BacktestInputException(GraphValidationResult validation) : EngineException("Invalid exact execution selections.")
{
    public GraphValidationResult Validation { get; } = validation;
}