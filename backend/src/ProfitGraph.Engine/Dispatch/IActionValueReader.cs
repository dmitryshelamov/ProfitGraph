using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Dispatch;

internal interface IActionValueReader
{
    public EngineValue ReadTrigger(CompiledInputBinding binding);
    public ResolvedEngineParameters ResolveParameters(CompiledTradingNode node);
}